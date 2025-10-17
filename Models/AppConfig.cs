using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace toggle_lang.Models
{
    /// <summary>
    /// 应用程序配置类
    /// </summary>
    public class AppConfig : INotifyPropertyChanged
    {
        private string _appName = string.Empty;
        private string _processName = string.Empty;
        private string _language = string.Empty;
        private bool _isEnabled = true;

        /// <summary>
        /// 应用程序名称
        /// </summary>
        public string AppName
        {
            get => _appName;
            set
            {
                if (_appName != value)
                {
                    _appName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 进程名称（不含.exe）
        /// </summary>
        public string ProcessName
        {
            get => _processName;
            set
            {
                if (_processName != value)
                {
                    _processName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 输入法语言（例如：zh-CN, en-US）
        /// </summary>
        public string Language
        {
            get => _language;
            set
            {
                if (_language != value)
                {
                    _language = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

