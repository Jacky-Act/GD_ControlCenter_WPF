using GD_ControlCenter_WPF.Models;
using System.IO;
using System.Text.Json;

/*
 * 文件名: JsonConfigService.cs
 * 描述: JSON 配置持久化服务类，负责将全局配置模型 AppConfig 序列化至本地磁盘。
 * 采用 System.Text.Json 实现高效读写，默认存储于用户本地应用数据目录（Local AppData），确保在不同用户账户下的数据隔离。
 * 维护指南: 修改配置存储路径需同步更新构造函数中的目录初始化逻辑；Save 方法默认开启 WriteIndented 以确保生成的 JSON 具有良好的可读性。
 */

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 本地配置管理服务：提供配置文件的异步加载与持久化保存功能。
    /// </summary>
    public class JsonConfigService
    {
        /// <summary>
        /// 配置文件在磁盘上的完整物理路径。
        /// </summary>
        private readonly string _filePath;

        /// <summary>
        /// 初始化配置服务。
        /// 自动检测并创建存储目录（%LocalAppData%\GD_ControlCenter），并确定配置文件路径。
        /// </summary>
        public JsonConfigService()
        {
            // 获取当前用户的 Local AppData 目录 (例如: C:\Users\Username\AppData\Local)
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 为当前软件创建专属的数据存放子文件夹
            string myAppFolder = Path.Combine(appDataFolder, "GD_ControlCenter");

            // 确保物理目录存在，若不存在则递归创建
            if (!Directory.Exists(myAppFolder))
            {
                Directory.CreateDirectory(myAppFolder);
            }

            // 定义最终的配置文件物理全路径
            _filePath = Path.Combine(myAppFolder, "config.json");
        }

        /// <summary>
        /// 从本地磁盘加载配置。
        /// </summary>
        /// <returns>
        /// 若文件存在且解析成功，返回反序列化后的 <see cref="AppConfig"/> 对象；
        /// 若文件不存在或解析失败，返回包含默认参数的新实例。
        /// </returns>
        public AppConfig Load()
        {
            try
            {
                // 如果文件尚未创建（首次运行），直接返回默认配置
                if (!File.Exists(_filePath)) return new AppConfig();

                string json = File.ReadAllText(_filePath);

                // 反序列化并执行空值检查
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch (Exception)
            {
                // 读取失败时返回默认配置，确保软件能够启动
                return new AppConfig();
            }
        }

        /// <summary>
        /// 将指定的配置状态持久化至磁盘。
        /// </summary>
        /// <param name="config">待保存的全局配置实体实例。</param>
        public void Save(AppConfig config)
        {
            try
            {
                // 配置序列化选项：开启缩进以提升 JSON 可读性
                var options = new JsonSerializerOptions { WriteIndented = true };

                string json = JsonSerializer.Serialize(config, options);

                // 执行 IO 写入
                File.WriteAllText(_filePath, json);
            }
            catch (Exception)
            {
                // TODO: 可以在此处添加日志埋点（如 NLog 或 Serilog），记录磁盘写入权限等错误
            }
        }
    }
}