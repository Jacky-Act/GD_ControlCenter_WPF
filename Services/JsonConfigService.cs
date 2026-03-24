using System;
using System.IO;
using System.Text.Json;
using GD_ControlCenter_WPF.Models;

namespace GD_ControlCenter_WPF.Services
{
    public class JsonConfigService
    {
        private readonly string _filePath;

        public JsonConfigService()
        {
            // 1. 获取当前用户的 Local AppData 目录 (例如: C:\Users\用户名\AppData\Local)
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 2. 为当前软件创建一个专属数据文件夹
            string myAppFolder = Path.Combine(appDataFolder, "GD_ControlCenter");

            // 3. 确保文件夹存在，不存在则自动创建
            if (!Directory.Exists(myAppFolder))
            {
                Directory.CreateDirectory(myAppFolder);
            }

            // 4. 最终的配置文件物理路径
            _filePath = Path.Combine(myAppFolder, "config.json");
        }

        // 读取配置
        public AppConfig Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new AppConfig();

                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig(); // 读取失败则返回默认值
            }
        }

        // 保存配置
        public void Save(AppConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // 日志埋点：记录保存失败
            }
        }
    }
}