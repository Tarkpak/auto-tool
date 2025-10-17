using System;
using System.Windows;

namespace toggle_lang
{
    public partial class LogWindow : Window
    {
        private int _logCount = 0;

        public LogWindow()
        {
            InitializeComponent();
        }

        public void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _logCount++;
                LogCountText.Text = _logCount.ToString();

                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                LogTextBox.AppendText($"[{timestamp}] {message}\n");

                // 自动滚动到底部
                if (AutoScrollCheckBox.IsChecked == true)
                {
                    LogScrollViewer.ScrollToEnd();
                }
            });
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            _logCount = 0;
            LogCountText.Text = "0";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 隐藏而不是关闭
            e.Cancel = true;
            Hide();
        }
    }
}

