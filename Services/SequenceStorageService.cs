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
        public void ExportToCsv(string filePath, List<SampleItemModel> sequence, List<string> activeElements)
        {
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);

            // 1. 写入表头
            var header = new List<string> { "样品名称", "类型", "重复次数" };
            header.AddRange(activeElements.Select(e => $"{e}浓度"));
            writer.WriteLine(string.Join(",", header));

            // 2. 写入数据行
            foreach (var item in sequence)
            {
                var row = new List<string> {
                item.SampleName,
                item.Type.ToString(),
                item.Repeats.ToString()
            };
                // 根据当前元素名单，按顺序取出浓度值
                foreach (var el in activeElements)
                {
                    var conc = item.ElementConcentrations.FirstOrDefault(c => c.ElementName == el)?.ConcentrationValue ?? "";
                    row.Add(conc);
                }
                writer.WriteLine(string.Join(",", row));
            }
        }

        // --- 新增：导入 CSV ---
        public List<SampleItemModel> ImportFromCsv(string filePath, out List<string> detectedElements)
        {
            var list = new List<SampleItemModel>();
            detectedElements = new List<string>();

            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 1) return list;

            // 解析表头获取元素名称
            var headers = lines[0].Split(',');
            for (int i = 3; i < headers.Length; i++)
            {
                detectedElements.Add(headers[i].Replace("浓度", ""));
            }

            // 解析数据行
            for (int i = 1; i < lines.Length; i++)
            {
                var cells = lines[i].Split(',');
                if (cells.Length < 3) continue;

                var item = new SampleItemModel
                {
                    SampleName = cells[0],
                    Type = Enum.TryParse(cells[1], out SampleType t) ? t : SampleType.待测液,
                    Repeats = int.TryParse(cells[2], out int r) ? r : 1
                };

                // 填充浓度数据
                for (int j = 0; j < detectedElements.Count; j++)
                {
                    item.ElementConcentrations.Add(new ElementConcentrationModel
                    {
                        ElementName = detectedElements[j],
                        ConcentrationValue = (cells.Length > j + 3) ? cells[j + 3] : ""
                    });
                }
                list.Add(item);
            }
            return list;
        }
    }
}