using System.Windows;
using System.Linq;

namespace toggle_lang
{
    public partial class App : Application
    {
        public static bool IsSilentStartup { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 检查是否有 --silent 参数（用于开机自启动时后台运行）
            IsSilentStartup = e.Args.Contains("--silent") || e.Args.Contains("-s");
        }
    }
}

