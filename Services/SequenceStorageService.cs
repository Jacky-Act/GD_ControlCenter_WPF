using GD_ControlCenter_WPF.Models.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GD_ControlCenter_WPF.Services
{
    public class SequenceStorageService
    {
        private readonly string _folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sequences");

        public SequenceStorageService()
        {
            if (!Directory.Exists(_folderPath)) Directory.CreateDirectory(_folderPath);
        }

        // 获取所有保存的模板名称
        public List<string> GetSavedTemplates()
        {
            var files = Directory.GetFiles(_folderPath, "*.seq");
            var names = new List<string>();
            foreach (var f in files) names.Add(Path.GetFileNameWithoutExtension(f));
            return names;
        }

        // 保存为专属模板
        public void SaveTemplate(string templateName, List<SampleItemModel> sequence)
        {
            string path = Path.Combine(_folderPath, $"{templateName}.seq");
            string json = JsonSerializer.Serialize(sequence, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        // 加载专属模板
        public List<SampleItemModel>? LoadTemplate(string templateName)
        {
            string path = Path.Combine(_folderPath, $"{templateName}.seq");
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<SampleItemModel>>(json);
        }

        // 简易版导出 CSV (可根据实际需求补充扩展)
        public void ExportToCsv(string filePath, List<SampleItemModel> sequence)
        {
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("样品名称,类型,重复次数");
            foreach (var item in sequence) writer.WriteLine($"{item.SampleName},{item.Type},{item.Repeats}");
        }
    }
}