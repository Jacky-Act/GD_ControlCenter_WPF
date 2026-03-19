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

        /// <summary>
        /// 异步导出单次全谱数据，直接在内存中构建符合格式要求的 XLSX 文件
        /// </summary>
        /// <param name="targetPath">要保存的完整文件路径</param>
        /// <param name="captureTime">记录时间</param>
        /// <param name="integrationTime">积分时间</param>
        /// <param name="avgCount">平均次数</param>
        /// <param name="wavelengths">波长数组</param>
        /// <param name="intensities">强度数组</param>
        /// <returns>返回生成的 XLSX 完整路径</returns>
        public async Task<string> ExportSingleSpectrumAsync(string targetPath, DateTime captureTime, float integrationTime,
            uint avgCount, double[] wavelengths, double[] intensities)
        {
            if (wavelengths == null || intensities == null || wavelengths.Length != intensities.Length)
                return string.Empty;

            return await Task.Run(() =>
            {
                ExcelPackage.License.SetNonCommercialPersonal("个人用户");

                using (var package = new ExcelPackage(new FileInfo(targetPath)))
                {
                    var sheet = package.Workbook.Worksheets.Add("单幅光谱");

                    // --- 1. 写入表头信息 (严格按照给定格式) ---
                    sheet.Cells["A1"].Value = "时间:";
                    sheet.Cells["B1"].Value = captureTime.ToString("yyyy/MM/dd HH:mm:ss");

                    sheet.Cells["A2"].Value = "积分时间:";
                    sheet.Cells["B2"].Value = "平均次数:";

                    sheet.Cells["A3"].Value = integrationTime;
                    sheet.Cells["B3"].Value = avgCount;

                    sheet.Cells["A5"].Value = "波长";
                    sheet.Cells["B5"].Value = "强度";

                    // --- 2. 批量写入具体数据 ---
                    int startRow = 6;

                    // 为了极大提升写入性能，先将数据转为二维数组，一次性写入 Excel
                    int dataCount = wavelengths.Length;
                    object[,] dataRange = new object[dataCount, 2];
                    for (int i = 0; i < dataCount; i++)
                    {
                        dataRange[i, 0] = wavelengths[i];
                        dataRange[i, 1] = intensities[i];
                    }

                    sheet.Cells[startRow, 1, startRow + dataCount - 1, 2].Value = dataRange;

                    // --- 3. 样式优化 ---
                    // 冻结前 5 行，向下滚动时表头固定
                    sheet.View.FreezePanes(6, 1);

                    // 字体加粗与居中对齐
                    sheet.Cells["A1:B2"].Style.Font.Bold = true;
                    sheet.Cells["A5:B5"].Style.Font.Bold = true;
                    sheet.Cells["A1:B5"].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                    // 数据区右对齐
                    sheet.Cells[startRow, 1, startRow + dataCount - 1, 2].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;

                    // 数字格式化 (波长保留2位小数，强度保留2位)
                    sheet.Cells[startRow, 1, startRow + dataCount - 1, 1].Style.Numberformat.Format = "0.00";
                    sheet.Cells[startRow, 2, startRow + dataCount - 1, 2].Style.Numberformat.Format = "0.00";

                    // 列宽自适应
                    sheet.Column(1).AutoFit();
                    sheet.Column(2).AutoFit();

                    package.Save();
                }

                return targetPath;
            });
        }

        /// <summary>
        /// 异步读取单次保存的 Excel 全谱文件
        /// </summary>
        /// <param name="filePath">XLSX 文件路径</param>
        /// <returns>解析成功返回 SpectralData，失败返回 null</returns>
        public async Task<Models.Spectrometer.SpectralData?> ImportSingleSpectrumAsync(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            return await Task.Run(() =>
            {
                try
                {
                    ExcelPackage.License.SetNonCommercialPersonal("个人用户");
                    using var package = new ExcelPackage(new FileInfo(filePath));
                    var sheet = package.Workbook.Worksheets.FirstOrDefault();

                    if (sheet == null) return null;

                    // 1. 读取 B1 单元格的时间字符串作为数据的身份标识
                    string timeStr = sheet.Cells["B1"].Text;
                    DateTime acqTime = DateTime.Now;
                    DateTime.TryParse(timeStr, out acqTime);

                    // 2. 计算有效数据行数 (跳过前 5 行表头)
                    int rowCount = sheet.Dimension.Rows;
                    if (rowCount < 6) return null;

                    int dataCount = rowCount - 5;
                    double[] wavelengths = new double[dataCount];
                    double[] intensities = new double[dataCount];

                    // 3. 批量读取 A 列(波长)和 B 列(强度)
                    for (int i = 0; i < dataCount; i++)
                    {
                        int row = i + 6;
                        double.TryParse(sheet.Cells[row, 1].Text, out wavelengths[i]);
                        double.TryParse(sheet.Cells[row, 2].Text, out intensities[i]);
                    }

                    // 4. 封装为标准实体返回
                    return new Models.Spectrometer.SpectralData
                    {
                        Wavelengths = wavelengths,
                        Intensities = intensities,
                        AcquisitionTime = acqTime,
                        SourceDeviceSerial = "Reference" // 特殊标记，代表这是参考图层数据
                    };
                }
                catch
                {
                    // 若文件被占用或格式被篡改导致解析失败，安全返回 null
                    return null;
                }
            });
        }

        /// <summary>
        /// 异步批量导出脚本光谱数据 (内存缓存一次性落盘)
        /// </summary>
        /// <param name="targetPath">XLSX 文件的完整路径</param>
        /// <param name="dataList">缓存在内存中的所有光谱数据</param>
        /// <param name="integrationTime">积分时间</param>
        /// <param name="averagingCount">平均次数</param>
        /// <returns>是否保存成功</returns>
        public async Task<bool> ExportScriptSpectraBulkAsync(string targetPath, List<Models.Spectrometer.SpectralData> dataList, float integrationTime, uint averagingCount)
        {
            if (dataList == null || dataList.Count == 0) return false;

            return await Task.Run(() =>
            {
                try
                {
                    ExcelPackage.License.SetNonCommercialPersonal("个人用户");

                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    using (var package = new ExcelPackage(new FileInfo(targetPath)))
                    {
                        var sheet = package.Workbook.Worksheets.Add("自动保存记录");
                        var firstFrame = dataList[0];

                        // --- 1. 写入 A 列 (表头与公共波长) ---
                        sheet.Cells["A1"].Value = "时间:";
                        sheet.Cells["A2"].Value = "积分时间:";
                        sheet.Cells["A3"].Value = "平均次数:";
                        sheet.Cells["A5"].Value = "波长";

                        int totalPoints = firstFrame.Wavelengths.Length;
                        object[,] waveData = new object[totalPoints, 1];
                        for (int i = 0; i < totalPoints; i++) waveData[i, 0] = firstFrame.Wavelengths[i];
                        sheet.Cells[6, 1, totalPoints + 5, 1].Value = waveData;

                        sheet.Cells[6, 1, totalPoints + 5, 1].Style.Numberformat.Format = "0.00";
                        sheet.Column(1).AutoFit();
                        sheet.View.FreezePanes(6, 2);

                        // --- 2. 批量循环写入所有列的数据 ---
                        for (int i = 0; i < dataList.Count; i++)
                        {
                            var frame = dataList[i];
                            int col = i + 2; // 第1次在B列(2)

                            sheet.Cells[1, col].Value = frame.AcquisitionTime.ToString("HH:mm:ss.fff"); // 加上毫秒，防止间隔太短时间一样
                            sheet.Cells[2, col].Value = integrationTime;
                            sheet.Cells[3, col].Value = averagingCount;
                            sheet.Cells[5, col].Value = $"强度_{i + 1}";

                            object[,] intensityData = new object[totalPoints, 1];
                            for (int j = 0; j < totalPoints; j++) intensityData[j, 0] = frame.Intensities[j];
                            sheet.Cells[6, col, totalPoints + 5, col].Value = intensityData;

                            sheet.Cells[6, col, totalPoints + 5, col].Style.Numberformat.Format = "0.00";
                            sheet.Column(col).AutoFit();
                        }

                        // 表头全局样式
                        sheet.Cells[1, 1, 5, dataList.Count + 1].Style.Font.Bold = true;
                        sheet.Cells[1, 1, 5, dataList.Count + 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                        // 核心：所有数据拼接完毕，一次性砸进硬盘！
                        package.Save();
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 异步批量导出时序图数据 (二维数组极速落盘)
        /// </summary>
        /// <param name="targetPath">XLSX 文件的完整路径</param>
        /// <param name="trackPointNames">追踪点的表头名称列表 (如 "峰值1(450.5nm)")</param>
        /// <param name="dataList">内存中缓存的所有时序快照数据</param>
        /// <param name="integrationTime">积分时间</param>
        /// <param name="averagingCount">平均次数</param>
        /// <returns>是否保存成功</returns>
        public async Task<bool> ExportTimeSeriesBulkAsync(
            string targetPath,
            System.Collections.Generic.List<string> trackPointNames,
            System.Collections.Generic.List<Models.Spectrometer.TimeSeriesSnapshot> dataList,
            float integrationTime,
            uint averagingCount)
        {
            // 防呆校验：如果没有数据或没有设置追踪点，直接不存
            if (dataList == null || dataList.Count == 0 || trackPointNames == null || trackPointNames.Count == 0)
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    ExcelPackage.License.SetNonCommercialPersonal("个人用户");
                    if (File.Exists(targetPath)) File.Delete(targetPath);

                    using (var package = new ExcelPackage(new FileInfo(targetPath)))
                    {
                        var sheet = package.Workbook.Worksheets.Add("时序图数据");

                        // --- 1. 写入文件头信息 (第 1-3 行) ---
                        sheet.Cells["A1"].Value = "任务开始时间:";
                        sheet.Cells["B1"].Value = dataList[0].Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

                        sheet.Cells["A2"].Value = "积分时间(ms):";
                        sheet.Cells["B2"].Value = integrationTime;

                        sheet.Cells["A3"].Value = "平均次数:";
                        sheet.Cells["B3"].Value = averagingCount;

                        // --- 2. 写入列名表头 (第 5 行) ---
                        sheet.Cells["A5"].Value = "时间 (Time)";
                        for (int i = 0; i < trackPointNames.Count; i++)
                        {
                            // B列对应 index 2，C列对应 index 3...
                            sheet.Cells[5, i + 2].Value = trackPointNames[i];
                        }

                        // --- 3. 准备二维矩阵，极速打包核心数据 ---
                        int rowCount = dataList.Count;
                        int colCount = trackPointNames.Count + 1; // 1列时间 + N列强度

                        // 创建一个巨大的二维数组矩阵，类型为 object 以兼容字符串时间与数字强度
                        object[,] exportDataMatrix = new object[rowCount, colCount];

                        for (int r = 0; r < rowCount; r++)
                        {
                            var snapshot = dataList[r];

                            // 第 0 列放时间
                            exportDataMatrix[r, 0] = snapshot.Timestamp.ToString("HH:mm:ss.fff");

                            // 第 1~N 列放强度
                            for (int c = 0; c < trackPointNames.Count; c++)
                            {
                                // 安全校验：防止运行期间用户动态删减点导致数组越界
                                if (c < snapshot.Intensities.Length)
                                    exportDataMatrix[r, c + 1] = snapshot.Intensities[c];
                                else
                                    exportDataMatrix[r, c + 1] = 0; // 缺失数据补 0
                            }
                        }

                        // --- 4. 一次性将二维矩阵砸进 Excel (从第 6 行 A 列开始) ---
                        // 语法解释：Cells[起始行, 起始列, 结束行, 结束列]
                        sheet.Cells[6, 1, rowCount + 5, colCount].Value = exportDataMatrix;

                        // --- 5. 格式美化 ---
                        // 表头加粗
                        sheet.Cells[1, 1, 5, colCount].Style.Font.Bold = true;
                        // 强度数据保留小数
                        sheet.Cells[6, 2, rowCount + 5, colCount].Style.Numberformat.Format = "0.00";
                        // 自动调整列宽
                        sheet.Cells[1, 1, rowCount + 5, colCount].AutoFitColumns();
                        // 冻结首列和前5行，方便滚动查看长数据
                        sheet.View.FreezePanes(6, 2);

                        package.Save();
                    }
                    return true;
                }
                catch
                {
                    // 若文件被占用或无权限写入
                    return false;
                }
            });
        }

        public void Dispose()
        {
            EndSession();
        }
    }
}