using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using toggle_lang.Models;
using toggle_lang.Services;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;
using System.Diagnostics;

namespace toggle_lang
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<AppConfig> _appConfigs = new();
        private WindowMonitor _windowMonitor = new();
        private bool _isMonitoring = false;
        private NotifyIcon? _notifyIcon;
        private string _currentProcessName = string.Empty;
        private string _currentAppName = string.Empty;
        private LogWindow? _logWindow;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApp();
        }

        private void InitializeApp()
        {
            // 初始化日志窗口
            _logWindow = new LogWindow();
            LogManager.Initialize(_logWindow);
            
            Debug.WriteLine("========================================");
            Debug.WriteLine("输入法语言自动切换器已启动");
            Debug.WriteLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Debug.WriteLine("========================================");

            // 加载配置
            LoadConfigs();

            // 初始化语言下拉框
            InitializeLanguageComboBox();

            // 设置DataGrid数据源
            AppConfigDataGrid.ItemsSource = _appConfigs;

            // 初始化DataGrid语言列的下拉框
            InitializeDataGridLanguageColumn();

            // 初始化托盘图标
            InitializeTrayIcon();

            // 设置窗口监控事件
            _windowMonitor.ActiveWindowChanged += WindowMonitor_ActiveWindowChanged;

            // 检查开机自启动状态
            AutoStartCheckBox.IsChecked = IsAutoStartEnabled();

            // 自动启动监控
            StartMonitoring();
            Debug.WriteLine("自动启动监控功能");
        }

        private List<LanguageItem> GetLanguageItems()
        {
            return new List<LanguageItem>
            {
                new LanguageItem { Code = "zh-CN", Name = "中文（简体）" },
                new LanguageItem { Code = "zh-TW", Name = "中文（繁体）" },
                new LanguageItem { Code = "en-US", Name = "English (US)" },
                new LanguageItem { Code = "en-GB", Name = "English (UK)" },
                new LanguageItem { Code = "ja-JP", Name = "日本語" },
                new LanguageItem { Code = "ko-KR", Name = "한국어" },
                new LanguageItem { Code = "fr-FR", Name = "Français" },
                new LanguageItem { Code = "de-DE", Name = "Deutsch" },
                new LanguageItem { Code = "es-ES", Name = "Español" },
                new LanguageItem { Code = "ru-RU", Name = "Русский" },
            };
        }

        private void InitializeLanguageComboBox()
        {
            var languages = GetLanguageItems();

            NewLanguageComboBox.ItemsSource = languages;
            NewLanguageComboBox.DisplayMemberPath = "Name";
            NewLanguageComboBox.SelectedValuePath = "Code";
            
            // 为快捷设置下拉框也设置相同的数据源
            QuickLanguageComboBox.ItemsSource = new List<LanguageItem>(languages);
            QuickLanguageComboBox.DisplayMemberPath = "Name";
            QuickLanguageComboBox.SelectedValuePath = "Code";
            QuickLanguageComboBox.SelectedIndex = 0;
        }

        private void InitializeDataGridLanguageColumn()
        {
            var languages = GetLanguageItems();
            LanguageColumn.ItemsSource = languages;
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "输入法语言自动切换器",
                Visible = true
            };

            // 尝试设置图标（如果没有图标文件，使用默认图标）
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico");
                Debug.WriteLine($"尝试加载托盘图标: {iconPath}");
                Debug.WriteLine($"图标文件是否存在: {System.IO.File.Exists(iconPath)}");
                
                if (System.IO.File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                    Debug.WriteLine("托盘图标加载成功");
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                    Debug.WriteLine("使用默认应用程序图标");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载托盘图标失败: {ex.Message}");
                _notifyIcon.Icon = SystemIcons.Information;
            }

            // 单击托盘图标切换显示/隐藏窗口
            _notifyIcon.Click += (s, e) =>
            {
                // 检查是否是鼠标左键单击
                if (e is MouseEventArgs mouseEvent && mouseEvent.Button == MouseButtons.Left)
                {
                    if (IsVisible && WindowState != WindowState.Minimized)
                    {
                        // 窗口可见且未最小化，则隐藏
                        Hide();
                    }
                    else
                    {
                        // 窗口隐藏或最小化，则显示
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    }
                }
            };

            // 右键菜单
            var contextMenu = new ContextMenuStrip();
            
            var showMenuItem = new ToolStripMenuItem("显示主窗口");
            showMenuItem.Click += (s, e) =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };
            
            var exitMenuItem = new ToolStripMenuItem("退出");
            exitMenuItem.Click += (s, e) =>
            {
                _notifyIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            };

            contextMenu.Items.Add(showMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void LoadConfigs()
        {
            _appConfigs.Clear();
            var configs = ConfigManager.LoadConfig();
            foreach (var config in configs)
            {
                config.PropertyChanged += Config_PropertyChanged;
                _appConfigs.Add(config);
            }
        }

        private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 当配置项属性发生变化时自动保存
            if (e.PropertyName == nameof(AppConfig.IsEnabled))
            {
                SaveConfigs();
            }
        }

        private void SaveConfigs()
        {
            ConfigManager.SaveConfig(_appConfigs.ToList());
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMonitoring)
            {
                StopMonitoring();
            }
            else
            {
                StartMonitoring();
            }
        }

        private void StartMonitoring()
        {
            _windowMonitor.Start();
            _isMonitoring = true;
            StartStopButton.Content = "停止监控";
            UpdateCurrentStatus();
        }

        private void StopMonitoring()
        {
            _windowMonitor.Stop();
            _isMonitoring = false;
            StartStopButton.Content = "启动监控";
        }

        private void WindowMonitor_ActiveWindowChanged(object? sender, string processName)
        {
            Dispatcher.Invoke(() =>
            {
                // 忽略本程序自身
                if (processName.Equals("auto-tool", StringComparison.OrdinalIgnoreCase))
                {
                    _currentProcessName = string.Empty;
                    _currentAppName = string.Empty;
                    return;
                }
                
                _currentProcessName = processName;
                
                var config = ConfigManager.GetConfigForProcess(processName, _appConfigs.ToList());
                if (config != null)
                {
                    InputMethodSwitcher.SwitchToLanguage(config.Language);
                    CurrentAppText.Text = $"{config.AppName} ({processName})";
                    _currentAppName = config.AppName;
                    QuickSetupBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CurrentAppText.Text = processName;
                    _currentAppName = processName;
                    
                    // 获取当前输入法语言并设置为默认选项
                    var currentLanguage = InputMethodSwitcher.GetCurrentLanguage();
                    if (!string.IsNullOrEmpty(currentLanguage))
                    {
                        QuickLanguageComboBox.SelectedValue = currentLanguage;
                    }
                    
                    // 显示快捷设置区域
                    QuickSetupBorder.Visibility = Visibility.Visible;
                }

                UpdateCurrentLanguage();
            });
        }

        private void UpdateCurrentStatus()
        {
            var processName = WindowMonitor.GetActiveProcessName();
            if (!string.IsNullOrEmpty(processName))
            {
                // 忽略本程序自身
                if (processName.Equals("auto-tool", StringComparison.OrdinalIgnoreCase))
                {
                    _currentProcessName = string.Empty;
                    _currentAppName = string.Empty;
                    return;
                }
                
                _currentProcessName = processName;
                
                var config = ConfigManager.GetConfigForProcess(processName, _appConfigs.ToList());
                if (config != null)
                {
                    CurrentAppText.Text = $"{config.AppName} ({processName})";
                    _currentAppName = config.AppName;
                    QuickSetupBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CurrentAppText.Text = processName;
                    _currentAppName = processName;
                    
                    // 获取当前输入法语言并设置为默认选项
                    var currentLanguage = InputMethodSwitcher.GetCurrentLanguage();
                    if (!string.IsNullOrEmpty(currentLanguage))
                    {
                        QuickLanguageComboBox.SelectedValue = currentLanguage;
                    }
                    
                    // 显示快捷设置区域
                    QuickSetupBorder.Visibility = Visibility.Visible;
                }
            }

            UpdateCurrentLanguage();
        }

        private void UpdateCurrentLanguage()
        {
            var language = InputMethodSwitcher.GetCurrentLanguage();
            CurrentLanguageText.Text = string.IsNullOrEmpty(language) ? "无" : language;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var appName = NewAppNameTextBox.Text.Trim();
            var processName = NewProcessNameTextBox.Text.Trim();
            var language = NewLanguageComboBox.SelectedValue?.ToString();

            if (string.IsNullOrEmpty(appName))
            {
                System.Windows.MessageBox.Show("请输入应用名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(processName))
            {
                System.Windows.MessageBox.Show("请输入进程名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(language))
            {
                System.Windows.MessageBox.Show("请选择输入法语言", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查是否已存在相同进程名的配置
            if (_appConfigs.Any(c => c.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)))
            {
                System.Windows.MessageBox.Show("该进程已存在配置，请直接编辑", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var config = new AppConfig
            {
                AppName = appName,
                ProcessName = processName,
                Language = language,
                IsEnabled = true
            };

            config.PropertyChanged += Config_PropertyChanged;
            _appConfigs.Add(config);
            SaveConfigs();

            // 清空输入框
            NewAppNameTextBox.Clear();
            NewProcessNameTextBox.Clear();
            NewLanguageComboBox.SelectedIndex = 0;

            System.Windows.MessageBox.Show("添加成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AppConfigDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // 延迟保存以确保绑定已更新
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 验证编辑的内容
                    if (e.EditingElement is System.Windows.Controls.TextBox textBox)
                    {
                        var newValue = textBox.Text.Trim();
                        if (string.IsNullOrEmpty(newValue))
                        {
                            System.Windows.MessageBox.Show("输入内容不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            AppConfigDataGrid.CancelEdit();
                            return;
                        }

                        // 如果编辑的是进程名称列，检查是否有重复
                        if (e.Column.Header.ToString() == "进程名称")
                        {
                            var currentConfig = e.Row.Item as AppConfig;
                            var duplicate = _appConfigs.FirstOrDefault(c => 
                                c != currentConfig && 
                                c.ProcessName.Equals(newValue, StringComparison.OrdinalIgnoreCase));
                            
                            if (duplicate != null)
                            {
                                System.Windows.MessageBox.Show("该进程名称已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                                AppConfigDataGrid.CancelEdit();
                                LoadConfigs(); // 重新加载配置以恢复原值
                                return;
                            }
                        }
                    }

                    SaveConfigs();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.DataContext is not AppConfig config)
                return;

            var result = System.Windows.MessageBox.Show(
                $"确定要删除 '{config.AppName}' 的配置吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _appConfigs.Remove(config);
                SaveConfigs();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // 最小化到托盘而不是关闭
            e.Cancel = true;
            Hide();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _windowMonitor.Stop();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }

        private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoStartCheckBox.IsChecked == true)
            {
                SetAutoStart(true);
            }
            else
            {
                SetAutoStart(false);
            }
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("ToggleLang") != null;
            }
            catch
            {
                return false;
            }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null)
                    return;

                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("ToggleLang", $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue("ToggleLang", false);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"设置开机自启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuickSetButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentProcessName))
            {
                System.Windows.MessageBox.Show("当前没有检测到应用程序", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var language = QuickLanguageComboBox.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(language))
            {
                System.Windows.MessageBox.Show("请选择输入法语言", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查是否已存在配置
            var existingConfig = _appConfigs.FirstOrDefault(c => 
                c.ProcessName.Equals(_currentProcessName, StringComparison.OrdinalIgnoreCase));

            if (existingConfig != null)
            {
                // 更新现有配置
                existingConfig.Language = language;
                existingConfig.IsEnabled = true;
                SaveConfigs();
                AppConfigDataGrid.Items.Refresh();
                
                System.Windows.MessageBox.Show(
                    $"已更新 '{existingConfig.AppName}' 的输入法语言为 {GetLanguageName(language)}",
                    "设置成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                // 创建新配置
                var config = new AppConfig
                {
                    AppName = _currentAppName,
                    ProcessName = _currentProcessName,
                    Language = language,
                    IsEnabled = true
                };

                config.PropertyChanged += Config_PropertyChanged;
                _appConfigs.Add(config);
                SaveConfigs();

                System.Windows.MessageBox.Show(
                    $"已为 '{_currentAppName}' 设置输入法语言为 {GetLanguageName(language)}",
                    "设置成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            // 立即切换输入法
            InputMethodSwitcher.SwitchToLanguage(language);
            
            // 隐藏快捷设置区域
            QuickSetupBorder.Visibility = Visibility.Collapsed;
        }

        private string GetLanguageName(string languageCode)
        {
            var languages = new Dictionary<string, string>
            {
                { "zh-CN", "中文（简体）" },
                { "zh-TW", "中文（繁体）" },
                { "en-US", "English (US)" },
                { "en-GB", "English (UK)" },
                { "ja-JP", "日本語" },
                { "ko-KR", "한국어" },
                { "fr-FR", "Français" },
                { "de-DE", "Deutsch" },
                { "es-ES", "Español" },
                { "ru-RU", "Русский" }
            };

            return languages.TryGetValue(languageCode, out var name) ? name : languageCode;
        }

        private void ShowLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogManager.ShowLogWindow();
        }
    }

    public class LanguageItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}

