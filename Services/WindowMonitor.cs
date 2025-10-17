using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace toggle_lang.Services
{
    /// <summary>
    /// 窗口监控服务
    /// </summary>
    public class WindowMonitor
    {
        private DispatcherTimer? _timer;
        private string _lastProcessName = string.Empty;

        public event EventHandler<string>? ActiveWindowChanged;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// 开始监控
        /// </summary>
        public void Start()
        {
            if (_timer != null)
                return;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
                _timer = null;
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                var processName = GetActiveProcessName();
                if (!string.IsNullOrEmpty(processName) && processName != _lastProcessName)
                {
                    _lastProcessName = processName;
                    ActiveWindowChanged?.Invoke(this, processName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"监控窗口时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前活动窗口的进程名称
        /// </summary>
        public static string GetActiveProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return string.Empty;

                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0)
                    return string.Empty;

                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

