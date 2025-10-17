using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;

// 辅助方法：同时输出到 Debug 和 Trace
static class DebugLog
{
    public static void WriteLine(string message)
    {
        // 只输出到 Trace，避免重复
        Trace.WriteLine(message);
    }
}

namespace toggle_lang.Services
{
    /// <summary>
    /// 输入法切换服务
    /// </summary>
    public class InputMethodSwitcher
    {
        // 记录每个进程的上次切换状态（进程ID -> 是否中文）
        // 使用进程ID而不是窗口句柄，因为同一进程的不同窗口共享输入法状态
        private static readonly Dictionary<uint, bool> _processStateCache = new();
        private static readonly object _cacheLock = new object();

        #region Win32 API - User32
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_SHIFT = 0x10;
        private const byte VK_SHIFT_BYTE = 0x10;
        #endregion

        #region Win32 API - IMM32
        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetConversionStatus(IntPtr hIMC, uint fdwConversion, uint fdwSentence);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);
        
        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        #endregion
        
        #region Windows Messages
        private const uint WM_IME_CONTROL = 0x0283;
        private const uint IMC_GETCONVERSIONMODE = 0x0001;
        private const uint IMC_SETCONVERSIONMODE = 0x0002;
        private const uint IMC_GETOPENSTATUS = 0x0005;
        private const uint IMC_SETOPENSTATUS = 0x0006;
        #endregion

        #region Constants
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const uint KLF_ACTIVATE = 0x00000001;

        // IME 转换模式标志
        private const uint IME_CMODE_ALPHANUMERIC = 0x0000;  // 英文模式
        private const uint IME_CMODE_NATIVE = 0x0001;        // 中文模式
        private const uint IME_CMODE_CHINESE = 0x0001;       // 中文模式（别名）
        private const uint IME_CMODE_KATAKANA = 0x0002;      // 片假名模式
        private const uint IME_CMODE_LANGUAGE = 0x0003;      // 语言模式掩码
        private const uint IME_CMODE_FULLSHAPE = 0x0008;     // 全角模式
        private const uint IME_CMODE_ROMAN = 0x0010;         // 罗马字模式
        private const uint IME_CMODE_CHARCODE = 0x0020;      // 字符代码输入
        private const uint IME_CMODE_HANJACONVERT = 0x0040;  // 汉字转换
        private const uint IME_CMODE_SOFTKBD = 0x0080;       // 软键盘
        private const uint IME_CMODE_NOCONVERSION = 0x0100;  // 无转换
        private const uint IME_CMODE_EUDC = 0x0200;          // EUDC转换
        private const uint IME_CMODE_SYMBOL = 0x0400;        // 符号转换
        private const uint IME_CMODE_FIXED = 0x0800;         // 固定转换
        #endregion

        /// <summary>
        /// 切换到指定语言的输入法
        /// </summary>
        public static bool SwitchToLanguage(string languageCode)
        {
            try
            {
                // 判断目标语言是否为中文系语言
                bool isChineseLanguage = IsChineseLanguage(languageCode);

                DebugLog.WriteLine($"==== 开始切换输入法 ====");
                DebugLog.WriteLine($"目标语言: {languageCode}, 是否中文: {isChineseLanguage}");

                // 获取当前活动窗口
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    DebugLog.WriteLine("错误: 无法获取前台窗口");
                    return false;
                }

                // 获取窗口线程ID
                uint threadId = GetWindowThreadProcessId(hwnd, out _);
                DebugLog.WriteLine($"前台窗口句柄: 0x{hwnd.ToInt64():X}, 线程ID: {threadId}");

                // 获取当前键盘布局
                IntPtr currentHkl = GetKeyboardLayout(threadId);
                int lcid = (int)currentHkl.ToInt64() & 0xFFFF;
                DebugLog.WriteLine($"当前键盘布局: 0x{currentHkl.ToInt64():X} (LCID: 0x{lcid:X} = {lcid})");

                // 只使用 IME API 切换输入模式（不改变键盘布局）
                bool result = SwitchIMEMode(hwnd, isChineseLanguage);
                
                if (result)
                {
                    DebugLog.WriteLine($"✓ 成功切换 IME 模式到 {(isChineseLanguage ? "中文" : "英文")}");
                }
                else
                {
                    DebugLog.WriteLine($"✗ 切换 IME 模式失败");
                }

                DebugLog.WriteLine($"==== 切换完成 ====\n");
                return result;
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"切换输入法异常: {ex.Message}");
                DebugLog.WriteLine($"堆栈: {ex.StackTrace}");
                    return false;
            }
        }

        /// <summary>
        /// 切换 IME 输入模式（中文/英文）
        /// </summary>
        private static bool SwitchIMEMode(IntPtr hwnd, bool toChinese)
        {
            try
            {
                // 获取输入法上下文
                IntPtr hIMC = ImmGetContext(hwnd);
                if (hIMC == IntPtr.Zero)
                {
                    DebugLog.WriteLine("无法获取 IME 上下文（窗口可能不支持 IME）");
                    // 对于不支持 IME 的窗口，尝试使用 Shift 键切换
                    return SwitchByShiftKey(hwnd, toChinese);
                }

                try
                {
                    // 获取当前状态
                    bool currentImeOpen = ImmGetOpenStatus(hIMC);
                    bool hasConversionStatus = ImmGetConversionStatus(hIMC, out uint conversion, out uint sentence);
                    
                    DebugLog.WriteLine($"当前 IME 状态 - 打开: {currentImeOpen}, 转换模式: 0x{conversion:X8}");

                    // 检查当前状态是否已经是目标状态
                    bool isCurrentlyChinese = currentImeOpen && (conversion & IME_CMODE_NATIVE) != 0;
                    if (isCurrentlyChinese == toChinese)
                    {
                        DebugLog.WriteLine($"已经是目标状态 ({(toChinese ? "中文" : "英文")})，无需切换");
                        return true;
                    }

                    if (toChinese)
                    {
                        // 切换到中文模式
                        DebugLog.WriteLine("正在切换到中文模式...");
                        
                        // 如果 IME 未打开，打开它
                        if (!currentImeOpen)
                        {
                            bool openResult = ImmSetOpenStatus(hIMC, true);
                            DebugLog.WriteLine($"打开 IME: {openResult}");
                            if (openResult)
                            {
                                return true;
                            }
                        }
                        
                        // 设置为中文输入模式
                        if (hasConversionStatus)
                        {
                            uint newConversion = conversion;
                            newConversion |= IME_CMODE_NATIVE;  // 启用中文模式
                            
                            bool convResult = ImmSetConversionStatus(hIMC, newConversion, sentence);
                            DebugLog.WriteLine($"设置中文转换模式: {convResult} (0x{newConversion:X8})");
                            
                            if (convResult)
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        // 切换到英文模式
                        DebugLog.WriteLine("正在切换到英文模式...");
                        
                        // 关闭 IME（最直接的方式）
                        bool closeResult = ImmSetOpenStatus(hIMC, false);
                        DebugLog.WriteLine($"关闭 IME: {closeResult}");
                        
                        if (closeResult)
                        {
                            return true;
                        }
                    }

                    // 如果 IME API 方式失败，尝试使用 Shift 键切换
                    DebugLog.WriteLine("IME API 方式失败，尝试使用 Shift 键切换");
                    return SwitchByShiftKey(hwnd, toChinese);
                }
                finally
                {
                    // 释放输入法上下文
                    ImmReleaseContext(hwnd, hIMC);
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"切换 IME 模式异常: {ex.Message}");
                // 发生异常时，尝试使用 Shift 键切换
                return SwitchByShiftKey(hwnd, toChinese);
            }
        }

        /// <summary>
        /// 通过模拟 Shift 键来切换 IME 中英文模式
        /// </summary>
        private static bool SwitchByShiftKey(IntPtr hwnd, bool toChinese)
        {
            try
            {
                DebugLog.WriteLine("尝试通过 Shift 键切换 IME 模式...");

                // 获取当前状态
                IntPtr hIMC = ImmGetContext(hwnd);
                bool? currentImeOpen = null;  // null 表示无法确定
                
                if (hIMC != IntPtr.Zero)
                {
                    currentImeOpen = ImmGetOpenStatus(hIMC);
                    ImmReleaseContext(hwnd, hIMC);
                    DebugLog.WriteLine($"当前 IME 打开状态: {currentImeOpen}, 目标: {(toChinese ? "中文" : "英文")}");
                }
                else
                {
                    DebugLog.WriteLine($"无法获取 IME 状态，将尝试切换到目标状态: {(toChinese ? "中文" : "英文")}");
                }

                // 判断是否需要切换
                bool needToggle = false;
                
                if (currentImeOpen.HasValue)
                {
                    // 能够获取状态：检查是否与目标一致
                    needToggle = (toChinese && !currentImeOpen.Value) || (!toChinese && currentImeOpen.Value);
                }
                else
                {
                    // 无法通过ImmGetContext获取状态，尝试使用IME窗口消息
                    DebugLog.WriteLine("无法获取 IME 上下文，尝试使用 IME 默认窗口");
                    
                    IntPtr hImeWnd = ImmGetDefaultIMEWnd(hwnd);
                    if (hImeWnd != IntPtr.Zero)
                    {
                        DebugLog.WriteLine($"获取到 IME 窗口: 0x{hImeWnd.ToInt64():X}");
                        
                        // 获取当前IME打开状态
                        IntPtr imeOpen = SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, IntPtr.Zero);
                        bool isImeOpen = imeOpen.ToInt32() != 0;
                        
                        // 获取转换模式（这才是真正的中英文状态）
                        IntPtr convMode = SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
                        uint conversionMode = (uint)convMode.ToInt32();
                        bool isNativeMode = (conversionMode & IME_CMODE_NATIVE) != 0;
                        
                        DebugLog.WriteLine($"IME 打开状态: {isImeOpen}, 转换模式: 0x{conversionMode:X}, Native模式: {isNativeMode}");
                        
                        // 判断当前是否为中文模式：IME打开 且 Native模式开启
                        bool isCurrentlyChinese = isImeOpen && isNativeMode;
                        DebugLog.WriteLine($"当前判断为: {(isCurrentlyChinese ? "中文模式" : "英文模式")}");
                        
                        needToggle = (toChinese != isCurrentlyChinese);
                        
                        if (!needToggle)
                        {
                            DebugLog.WriteLine($"✓ 当前状态已是目标状态: {(toChinese ? "中文" : "英文")}");
                            return true;
                        }
                        
                        DebugLog.WriteLine($"需要切换: {(isCurrentlyChinese ? "中文" : "英文")} -> {(toChinese ? "中文" : "英文")}");
                    }
                    else
                    {
                        // 完全无法获取IME信息
                        uint tid = GetWindowThreadProcessId(hwnd, out _);
                        IntPtr hkl = GetKeyboardLayout(tid);
                        long hklValue = hkl.ToInt64();
                        int lcid = (int)(hklValue & 0xFFFF);
                        
                        DebugLog.WriteLine($"无法获取 IME 窗口 - HKL: 0x{hklValue:X}, LCID: 0x{lcid:X}");
                        
                        // 判断当前键盘布局是否为中文
                        bool isChineseLayout = (lcid == 0x0804 || lcid == 0x0404 || lcid == 0x0C04);
                        
                        if (!isChineseLayout)
                        {
                            if (toChinese)
                            {
                                DebugLog.WriteLine("非中文键盘布局，无法切换到中文模式");
                                return false;
                            }
                            else
                            {
                                DebugLog.WriteLine("非中文键盘布局，已经是英文状态");
                                return true;
                            }
                        }
                        
                        // 作为最后手段，尝试按Shift键
                        DebugLog.WriteLine("使用Shift键作为最后手段");
                        needToggle = true;
                    }
                }
                
                if (needToggle)
                {
                    DebugLog.WriteLine($"开始切换 IME 模式到: {(toChinese ? "中文" : "英文")}");
                    
                    // 尝试使用IME窗口消息设置
                    IntPtr hImeWnd = ImmGetDefaultIMEWnd(hwnd);
                    if (hImeWnd != IntPtr.Zero)
                    {
                        DebugLog.WriteLine("使用 IME 窗口消息设置状态");
                        
                        if (toChinese)
                        {
                            // 设置为中文：打开IME 并 设置Native模式
                            DebugLog.WriteLine("设置 IME 为打开状态");
                            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETOPENSTATUS, (IntPtr)1);
                            System.Threading.Thread.Sleep(50);
                            
                            // 获取当前转换模式
                            IntPtr currentMode = SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
                            uint convMode = (uint)currentMode.ToInt32();
                            
                            // 添加 Native 标志
                            convMode |= IME_CMODE_NATIVE;
                            DebugLog.WriteLine($"设置转换模式为: 0x{convMode:X}");
                            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)convMode);
                        }
                        else
                        {
                            // 设置为英文：关闭IME 或 清除Native模式
                            DebugLog.WriteLine("设置 IME 为关闭状态");
                            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETOPENSTATUS, (IntPtr)0);
                        }
                        
                        // 等待生效
                        System.Threading.Thread.Sleep(100);
                        
                        // 验证是否成功
                        IntPtr imeOpen = SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, IntPtr.Zero);
                        IntPtr convModeResult = SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
                        bool actualOpen = imeOpen.ToInt32() != 0;
                        bool actualNative = ((uint)convModeResult.ToInt32() & IME_CMODE_NATIVE) != 0;
                        bool actualChinese = actualOpen && actualNative;
                        
                        DebugLog.WriteLine($"验证: IME={actualOpen}, Native={actualNative}, 最终={( actualChinese ? "中文" : "英文")}");
                        
                        if (actualChinese == toChinese)
                        {
                            DebugLog.WriteLine("✓ IME 消息方式切换成功");
                            return true;
                        }
                        else
                        {
                            DebugLog.WriteLine("⚠ IME 消息方式未达到预期，尝试 Shift 键");
                        }
                    }
                    
                    // 如果IME窗口方式失败，使用Shift键
                    DebugLog.WriteLine("使用 Shift 键切换");
                    PressShiftKey();
                    System.Threading.Thread.Sleep(100);
                    
                    DebugLog.WriteLine("切换完成");
                    return true;
                }
                else
                {
                    DebugLog.WriteLine("状态已经一致，无需切换");
                return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"Shift 键切换失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 按一次Shift键（用于切换IME）
        /// </summary>
        private static void PressShiftKey()
        {
            // 尝试 SendInput
            INPUT[] inputs = new INPUT[2];
            
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = VK_SHIFT;
            inputs[0].u.ki.dwFlags = 0;
            
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_SHIFT;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            
            uint result = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            
            if (result == 0)
            {
                // SendInput 失败，使用 keybd_event
                keybd_event(VK_SHIFT_BYTE, 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(10);
                keybd_event(VK_SHIFT_BYTE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        /// <summary>
        /// 判断是否为中文系语言
        /// </summary>
        private static bool IsChineseLanguage(string languageCode)
        {
            return languageCode.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) ||
                   languageCode.Equals("zh", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断是否为英文语言
        /// </summary>
        private static bool IsEnglishLanguage(string languageCode)
        {
            return languageCode.StartsWith("en-", StringComparison.OrdinalIgnoreCase) ||
                   languageCode.Equals("en", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取键盘布局句柄
        /// </summary>
        private static IntPtr GetKeyboardLayoutHandle(string languageCode)
        {
            try
            {
                // 将语言代码转换为LCID
                var culture = CultureInfo.GetCultureInfo(languageCode);
                var lcid = culture.LCID;

                // 格式化为键盘布局ID（8位十六进制）
                var klid = lcid.ToString("X8");

                // 尝试加载键盘布局
                IntPtr hkl = LoadKeyboardLayout(klid, KLF_ACTIVATE);
                
                // 如果加载失败，尝试使用简化格式（仅语言ID）
                if (hkl == IntPtr.Zero)
                {
                    var shortKlid = (lcid & 0xFFFF).ToString("X4");
                    klid = shortKlid.PadLeft(8, '0');
                    hkl = LoadKeyboardLayout(klid, KLF_ACTIVATE);
                }

                return hkl;
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"获取键盘布局句柄失败: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 获取当前输入法语言代码
        /// </summary>
        public static string GetCurrentLanguage()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return string.Empty;

                // 首先尝试获取 IME 状态
                IntPtr hIMC = ImmGetContext(hwnd);
                if (hIMC != IntPtr.Zero)
                {
                    try
                    {
                        // 获取 IME 打开状态
                        bool isImeOpen = ImmGetOpenStatus(hIMC);
                        
                        // 获取转换模式
                        if (ImmGetConversionStatus(hIMC, out uint conversion, out _))
                        {
                            // 如果 IME 打开且处于中文模式
                            if (isImeOpen && (conversion & IME_CMODE_NATIVE) != 0)
                            {
                                // 获取键盘布局来确定具体的中文语言
                GetWindowThreadProcessId(hwnd, out uint threadId);
                IntPtr hkl = GetKeyboardLayout(threadId);
                                int lcid = (int)(hkl.ToInt64() & 0xFFFF);
                var culture = CultureInfo.GetCultureInfo(lcid);

                                // 如果是中文键盘布局，返回具体的语言代码
                                if (culture.Name.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                                {
                return culture.Name;
                                }
                                
                                // 默认返回简体中文
                                return "zh-CN";
                            }
                            else
                            {
                                // IME 关闭或英文模式，返回英文
                                return "en-US";
                            }
                        }
                    }
                    finally
                    {
                        ImmReleaseContext(hwnd, hIMC);
                    }
                }

                // 如果无法获取 IME 状态，则使用键盘布局
                GetWindowThreadProcessId(hwnd, out uint tid);
                IntPtr hklDefault = GetKeyboardLayout(tid);
                long hklValue = hklDefault.ToInt64();
                int lcidDefault = (int)(hklValue & 0xFFFF);
                
                DebugLog.WriteLine($"GetCurrentLanguage - HKL: 0x{hklValue:X}, LCID: 0x{lcidDefault:X} ({lcidDefault})");
                
                // 检查 LCID 是否有效
                if (lcidDefault == 0 || lcidDefault == 0x0400)
                {
                    // 0x0400 是"进程或线程默认值"，需要特殊处理
                    // 尝试从高位获取
                    int highWord = (int)((hklValue >> 16) & 0xFFFF);
                    if (highWord != 0 && highWord != 0x0400)
                    {
                        lcidDefault = highWord;
                        DebugLog.WriteLine($"使用高 16 位作为 LCID: 0x{lcidDefault:X} ({lcidDefault})");
                    }
                    else
                    {
                        DebugLog.WriteLine("无法从键盘布局获取有效的 LCID");
                        return string.Empty;
                    }
                }
                
                var cultureDefault = CultureInfo.GetCultureInfo(lcidDefault);
                return cultureDefault.Name;
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"获取当前语言失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 获取系统中可用的输入法语言列表
        /// </summary>
        public static string[] GetAvailableLanguages()
        {
            try
            {
                return CultureInfo.GetCultures(CultureTypes.InstalledWin32Cultures)
                    .Select(c => c.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .OrderBy(name => name)
                    .ToArray();
            }
            catch
            {
                return new[] { "zh-CN", "en-US" };
            }
        }
    }
}

