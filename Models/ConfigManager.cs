using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace toggle_lang.Models
{
    /// <summary>
    /// 配置管理器
    /// </summary>
    public class ConfigManager
    {
        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ToggleLang",
            "config.json"
        );

        /// <summary>
        /// 加载配置
        /// </summary>
        public static List<AppConfig> LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    return JsonConvert.DeserializeObject<List<AppConfig>>(json) ?? new List<AppConfig>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            }

            return new List<AppConfig>();
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public static void SaveConfig(List<AppConfig> configs)
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(configs, Formatting.Indented);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定进程的配置
        /// </summary>
        public static AppConfig? GetConfigForProcess(string processName, List<AppConfig> configs)
        {
            return configs.FirstOrDefault(c => 
                c.IsEnabled && 
                c.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
        }
    }
}

