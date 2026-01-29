using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml; // 需要安装 EPPlus NuGet 包

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 通用数据导出服务：负责 IO 流管理与格式转换，不涉及具体业务格式
    /// </summary>
    public class ExcelExportService : IDisposable
    {
        private StreamWriter? _writer;
        private string? _currentCsvPath;

        /// <summary>
        /// 开启一个新的写入会话
        /// </summary>
        /// <param name="fullPath">用户保存的文件完整路径</param>
        public void StartSession(string fullPath)
        {
            EndSession(); // 确保清理旧会话

            // 临时存储为 CSV 格式以支持流式追加
            _currentCsvPath = Path.ChangeExtension(fullPath, ".csv");

            // 使用 UTF-8 with BOM 确保 Excel 打开 CSV 不乱码
            _writer = new StreamWriter(_currentCsvPath, false, Encoding.UTF8);
        }

        /// <summary>
        /// 写入一行文本数据
        /// </summary>
        /// <param name="csvLine">已由逻辑层格式化好的 CSV 行字符串（例如 "val1,val2,val3"）</param>
        public void WriteLine(string csvLine)
        {
            if (_writer == null) return;

            _writer.WriteLine(csvLine);

            // 实时刷新缓冲区，保证数据物理落地
            _writer.Flush();
        }

        /// <summary>
        /// 结束写入会话，释放文件锁
        /// </summary>
        public void EndSession()
        {
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Close();
                _writer.Dispose();
                _writer = null;
            }
        }

        /// <summary>
        /// 异步将 CSV 转换为真正的 XLSX 格式
        /// </summary>
        /// <param name="deleteCsv">转换完成后是否删除原始 CSV</param>
        /// <returns>返回生成的 XLSX 完整路径</returns>
        public async Task<string> ConvertToExcelAsync(bool deleteCsv = true)
        {
            if (string.IsNullOrEmpty(_currentCsvPath) || !File.Exists(_currentCsvPath))
                return string.Empty;

            string xlsxPath = Path.ChangeExtension(_currentCsvPath, ".xlsx");

            await Task.Run(() =>
            {
                ExcelPackage.License.SetNonCommercialPersonal("个人用户");  // 配置EPPlus非商业用途许可证（适配EPPlus 8+版本）  

                using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
                {
                    var sheet = package.Workbook.Worksheets.Add("Data Log");

                    // 使用 EPPlus 快速导入 CSV
                    var format = new ExcelTextFormat
                    {
                        Delimiter = ',',
                        Encoding = Encoding.UTF8
                    };

                    sheet.Cells["A1"].LoadFromText(new FileInfo(_currentCsvPath), format);

                    // 基础样式：首行冻结
                    sheet.View.FreezePanes(2, 1);

                    package.Save();
                }
            });

            if (deleteCsv && File.Exists(_currentCsvPath))
            {
                try { File.Delete(_currentCsvPath); } catch { /* 忽略删除失败 */ }
            }

            return xlsxPath;
        }

        public void Dispose()
        {
            EndSession();
        }
    }
}