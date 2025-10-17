using System;
using System.Diagnostics;

namespace toggle_lang.Services
{
    /// <summary>
    /// 日志管理器
    /// </summary>
    public class LogManager
    {
        private static LogWindow? _logWindow;
        private static LogTraceListener? _traceListener;

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        public static void Initialize(LogWindow logWindow)
        {
            _logWindow = logWindow;
            
            // 移除旧的监听器
            if (_traceListener != null)
            {
                Trace.Listeners.Remove(_traceListener);
            }

            // 添加新的监听器
            _traceListener = new LogTraceListener(_logWindow);
            Trace.Listeners.Add(_traceListener);
        }

        /// <summary>
        /// 显示日志窗口
        /// </summary>
        public static void ShowLogWindow()
        {
            _logWindow?.Show();
            _logWindow?.Activate();
        }

        /// <summary>
        /// 自定义 TraceListener
        /// </summary>
        private class LogTraceListener : TraceListener
        {
            private readonly LogWindow _logWindow;

            public LogTraceListener(LogWindow logWindow)
            {
                _logWindow = logWindow;
            }

            public override void Write(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    _logWindow.AppendLog(message);
                }
            }

            public override void WriteLine(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    _logWindow.AppendLog(message);
                }
            }
        }
    }
}

