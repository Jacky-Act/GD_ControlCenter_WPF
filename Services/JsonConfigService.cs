using GD_ControlCenter_WPF.Models;
using System.IO;
using System.Text.Json;

namespace GD_ControlCenter_WPF.Services
{
    public class JsonConfigService
    {
        private readonly string _filePath;
        private readonly string _appFolder;

        public JsonConfigService()
        {
            _appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GD_ControlCenter");
            if (!Directory.Exists(_appFolder)) Directory.CreateDirectory(_appFolder);
            _filePath = Path.Combine(_appFolder, "config.json");
        }

        public AppConfig Load()
        {
            if (!File.Exists(_filePath)) return new AppConfig();
            try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_filePath)) ?? new AppConfig(); }
            catch { return new AppConfig(); }
        }

        public void Save(AppConfig config)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void SaveResults(List<GD_ControlCenter_WPF.Models.Messages.SampleItemModel> results)
        {
            string resFolder = Path.Combine(_appFolder, "Results");
            if (!Directory.Exists(resFolder)) Directory.CreateDirectory(resFolder);
            string path = Path.Combine(resFolder, $"Result_{DateTime.Now:yyyyMMdd_HHmm}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}