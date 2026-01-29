using GD_ControlCenter_WPF.Models.Platform3D;
using System.IO;
using System.Text.Json;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    public class PlatformStorageService
    {
        private readonly string _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform_position.json");

        /// <summary>
        /// 异步加载位置和状态数据
        /// </summary>
        public async Task<PlatformSavedData?> LoadAsync()
        {
            if (!File.Exists(_filePath)) return null;
            try
            {
                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<PlatformSavedData>(json);
            }
            catch { return null; }
        }

        /// <summary>
        /// 异步保存位置和状态数据
        /// </summary>
        public async Task SaveAsync(PlatformPosition pos, PlatformStatus status)
        {
            // 确保文件所在的文件夹路径存在
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var data = new PlatformSavedData
            {
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                IsAtMin = status.IsAtMin,
                IsAtMax = status.IsAtMax
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            // 执行文件的“创建”或“更新”
            await File.WriteAllTextAsync(_filePath, json);  
        }
    }

    // 专门用于序列化的简单数据契约
    public class PlatformSavedData
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public Dictionary<AxisType, bool> IsAtMin { get; set; } = new();
        public Dictionary<AxisType, bool> IsAtMax { get; set; } = new();
    }
}
