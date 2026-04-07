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
            // 配置文件存储在程序运行目录下
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
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

        // 将连续进样中序列数据存成JSON
        public void SaveResults(List<GD_ControlCenter_WPF.Models.Messages.SampleItemModel> results)
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string fileName = $"Result_{DateTime.Now:yyyyMMdd_HHmm}.json";
                string path = Path.Combine(folder, fileName);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(results, options);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存结果失败: {ex.Message}");
            }
        }
    }
}