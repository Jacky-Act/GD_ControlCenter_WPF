using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml; // 依赖 EPPlus NuGet 包
using GD_ControlCenter_WPF.Models.Spectrometer;

/*
 * 文件名: ExcelExportService.cs
 * 模块: 数据持久化服务 (Data Persistence Services)
 * 描述: 通用数据导出引擎。负责将光谱数据转换为标准化的 XLSX 和 CSV 文件。
 * 架构规范:
 * 1. 【性能至上】：操作 Excel 时严禁使用 for 循环对单元格 (Cells) 逐一赋值！
 * 必须先在内存中构建二维数组 (object[,])，然后调用 EPPlus 一次性砸入范围 (Range) 中，这是百万级数据秒级落盘的关键。
 * 2. 【异步非阻塞】：所有 IO 密集型操作必须包裹在 Task.Run 中，绝不允许卡死 UI 线程。
 * 3. 【资源管控】：继承 IDisposable，确保流对象 (StreamWriter) 及时释放，防止文件锁死。
 */

namespace GD_ControlCenter_WPF.Services
{
    public class ExcelExportService : IDisposable
    {
        #region 1. 状态与流对象

        private StreamWriter? _writer;
        private string? _currentCsvPath;

        #endregion

        #region 2. 传统 CSV 流式追加引擎 (Stream Append)

        /// <summary>
        /// 开启一个新的流式写入会话。
        /// 场景：适用于无法一次性放入内存的超大范围持续记录，先落盘成 CSV。
        /// </summary>
        public void StartSession(string fullPath)
        {
            EndSession(); // 防呆：确保清理旧会话

            // 临时存储为 CSV 格式以支持流式极速追加
            _currentCsvPath = Path.ChangeExtension(fullPath, ".csv");

            // 使用 UTF-8 with BOM 确保后续直接用 Excel 打开 CSV 不会中文乱码
            _writer = new StreamWriter(_currentCsvPath, false, Encoding.UTF8);
        }

        /// <summary>
        /// 写入单行数据。
        /// </summary>
        public void WriteLine(string csvLine)
        {
            if (_writer == null) return;
            _writer.WriteLine(csvLine);
            _writer.Flush(); // 实时刷新缓冲区，防止软件突然崩溃导致数据丢失
        }

        /// <summary>
        /// 结束写入会话，释放系统级文件锁。
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
        /// 异步将临时 CSV 文件正式转换为带样式的 XLSX 格式。
        /// </summary>
        public async Task<string> ConvertToExcelAsync(bool deleteCsv = true)
        {
            if (string.IsNullOrEmpty(_currentCsvPath) || !File.Exists(_currentCsvPath))
                return string.Empty;

            string xlsxPath = Path.ChangeExtension(_currentCsvPath, ".xlsx");

            await Task.Run(() =>
            {
                // EPPlus 8+ 版本强制要求声明非商业用途许可
                ExcelPackage.License.SetNonCommercialPersonal("个人用户");

                using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
                {
                    var sheet = package.Workbook.Worksheets.Add("Data Log");

                    // 利用 EPPlus 原生支持快速导入 CSV，速度远快于逐行读取
                    var format = new ExcelTextFormat { Delimiter = ',', Encoding = Encoding.UTF8 };
                    sheet.Cells["A1"].LoadFromText(new FileInfo(_currentCsvPath), format);

                    sheet.View.FreezePanes(2, 1);
                    package.Save();
                }
            });

            if (deleteCsv && File.Exists(_currentCsvPath))
            {
                try { File.Delete(_currentCsvPath); } catch { /* 容错：忽略删除失败 */ }
            }

            return xlsxPath;
        }

        #endregion

        #region 3. 内存二维矩阵直接落盘引擎 (Bulk Memory Insert)

        /// <summary>
        /// 异步导出【单次全谱数据】。直接在内存中构建符合格式要求的 XLSX 文件。
        /// </summary>
        public async Task<string> ExportSingleSpectrumAsync(string targetPath, DateTime captureTime, float integrationTime, uint avgCount, double[] wavelengths, double[] intensities)
        {
            if (wavelengths == null || intensities == null || wavelengths.Length != intensities.Length)
                return string.Empty;

            return await Task.Run(() =>
            {
                ExcelPackage.License.SetNonCommercialPersonal("个人用户");

                using (var package = new ExcelPackage(new FileInfo(targetPath)))
                {
                    var sheet = package.Workbook.Worksheets.Add("单幅光谱");

                    // --- 1. 写入表头信息 (Metadata) ---
                    sheet.Cells["A1"].Value = "时间:";
                    sheet.Cells["B1"].Value = captureTime.ToString("yyyy/MM/dd HH:mm:ss");
                    sheet.Cells["A2"].Value = "积分时间:";
                    sheet.Cells["B2"].Value = "平均次数:";
                    sheet.Cells["A3"].Value = integrationTime;
                    sheet.Cells["B3"].Value = avgCount;
                    sheet.Cells["A5"].Value = "波长";
                    sheet.Cells["B5"].Value = "强度";

                    // --- 2. 二维矩阵极速注入 (Core Magic) ---
                    int startRow = 6;
                    int dataCount = wavelengths.Length;

                    // 【优化说明】：先分配 object[,]，在托管内存中装填数据，最后调用 EPPlus 
                    // 的 Range 属性一次性复制到底层。耗时从几秒钟缩短至毫秒级。
                    object[,] dataRange = new object[dataCount, 2];
                    for (int i = 0; i < dataCount; i++)
                    {
                        dataRange[i, 0] = wavelengths[i];
                        dataRange[i, 1] = intensities[i];
                    }
                    sheet.Cells[startRow, 1, startRow + dataCount - 1, 2].Value = dataRange;

                    // --- 3. 样式优化 (Formatting) ---
                    sheet.View.FreezePanes(6, 1); // 冻结前 5 行表头
                    sheet.Cells["A1:B2"].Style.Font.Bold = true;
                    sheet.Cells["A5:B5"].Style.Font.Bold = true;
                    sheet.Cells["A1:B5"].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    sheet.Cells[startRow, 1, startRow + dataCount - 1, 2].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                    sheet.Cells[startRow, 1, startRow + dataCount - 1, 1].Style.Numberformat.Format = "0.00";
                    sheet.Cells[startRow, 2, startRow + dataCount - 1, 2].Style.Numberformat.Format = "0.00";
                    sheet.Column(1).AutoFit();
                    sheet.Column(2).AutoFit();

                    package.Save();
                }

                return targetPath;
            });
        }

        /// <summary>
        /// 异步批量导出【自动化脚本获取的多帧全谱数据】。
        /// </summary>
        public async Task<bool> ExportScriptSpectraBulkAsync(string targetPath, List<SpectralData> dataList, float integrationTime, uint averagingCount)
        {
            if (dataList == null || dataList.Count == 0) return false;

            return await Task.Run(() =>
            {
                try
                {
                    ExcelPackage.License.SetNonCommercialPersonal("个人用户");

                    if (File.Exists(targetPath)) File.Delete(targetPath);

                    using (var package = new ExcelPackage(new FileInfo(targetPath)))
                    {
                        var sheet = package.Workbook.Worksheets.Add("自动保存记录");
                        var firstFrame = dataList[0];

                        // --- 1. 写入基准波长列 (A列) ---
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
                        sheet.View.FreezePanes(6, 2); // 冻结首列和表头

                        // --- 2. 矩阵遍历：按列写入每一帧的强度数据 ---
                        for (int i = 0; i < dataList.Count; i++)
                        {
                            var frame = dataList[i];
                            int col = i + 2; // 第1次写在B列(index 2)

                            // 写入该帧的元数据
                            sheet.Cells[1, col].Value = frame.AcquisitionTime.ToString("HH:mm:ss.fff");
                            sheet.Cells[2, col].Value = integrationTime;
                            sheet.Cells[3, col].Value = averagingCount;
                            sheet.Cells[5, col].Value = $"强度_{i + 1}";

                            // 【核心优化】：依然采用一维矩阵块级写入
                            object[,] intensityData = new object[totalPoints, 1];
                            for (int j = 0; j < totalPoints; j++) intensityData[j, 0] = frame.Intensities[j];
                            sheet.Cells[6, col, totalPoints + 5, col].Value = intensityData;

                            sheet.Cells[6, col, totalPoints + 5, col].Style.Numberformat.Format = "0.00";
                            sheet.Column(col).AutoFit();
                        }

                        // 全局表头样式居中加粗
                        sheet.Cells[1, 1, 5, dataList.Count + 1].Style.Font.Bold = true;
                        sheet.Cells[1, 1, 5, dataList.Count + 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

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
        /// 异步批量导出【时序图轨迹数据】。
        /// 行对应时间，列对应各个特征峰。
        /// </summary>
        public async Task<bool> ExportTimeSeriesBulkAsync(string targetPath, List<string> trackPointNames, List<TimeSeriesSnapshot> dataList, float integrationTime, uint averagingCount)
        {
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

                        // --- 1. 写入任务头信息 ---
                        sheet.Cells["A1"].Value = "任务开始时间:";
                        sheet.Cells["B1"].Value = dataList[0].Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                        sheet.Cells["A2"].Value = "积分时间(ms):";
                        sheet.Cells["B2"].Value = integrationTime;
                        sheet.Cells["A3"].Value = "平均次数:";
                        sheet.Cells["B3"].Value = averagingCount;

                        // --- 2. 写入追踪点列名 ---
                        sheet.Cells["A5"].Value = "时间 (Time)";
                        for (int i = 0; i < trackPointNames.Count; i++)
                        {
                            sheet.Cells[5, i + 2].Value = trackPointNames[i];
                        }

                        // --- 3. 构建全域大矩阵 ---
                        // 预分配大块托管内存。行：时间抽样点；列：1列时间 + N列峰值强度
                        int rowCount = dataList.Count;
                        int colCount = trackPointNames.Count + 1;
                        object[,] exportDataMatrix = new object[rowCount, colCount];

                        for (int r = 0; r < rowCount; r++)
                        {
                            var snapshot = dataList[r];
                            exportDataMatrix[r, 0] = snapshot.Timestamp.ToString("HH:mm:ss.fff");

                            for (int c = 0; c < trackPointNames.Count; c++)
                            {
                                // 防越界保护：防止因 UI 动态删减峰线导致索引越界
                                exportDataMatrix[r, c + 1] = (c < snapshot.Intensities.Length) ? snapshot.Intensities[c] : 0;
                            }
                        }

                        // 一次性矩阵拍入，性能巅峰
                        sheet.Cells[6, 1, rowCount + 5, colCount].Value = exportDataMatrix;

                        // --- 4. 美化格式 ---
                        sheet.Cells[1, 1, 5, colCount].Style.Font.Bold = true;
                        sheet.Cells[6, 2, rowCount + 5, colCount].Style.Numberformat.Format = "0.00";
                        sheet.Cells[1, 1, rowCount + 5, colCount].AutoFitColumns();
                        sheet.View.FreezePanes(6, 2);

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

        #endregion

        #region 4. 反向解析引擎 (Import)

        /// <summary>
        /// 异步读取系统导出的“单次保存全谱图” Excel 文件。
        /// 职责：将其逆向反序列化为 SpectralData 实体，用于前端 UI 的“参考图层”比对。
        /// </summary>
        public async Task<SpectralData?> ImportSingleSpectrumAsync(string filePath)
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

                    // 读取元数据：抓取创建时间
                    string timeStr = sheet.Cells["B1"].Text;
                    DateTime.TryParse(timeStr, out DateTime acqTime);

                    // 数据有效性验证：跳过前 5 行表头，评估核心数据行数
                    int rowCount = sheet.Dimension.Rows;
                    if (rowCount < 6) return null;

                    int dataCount = rowCount - 5;
                    double[] wavelengths = new double[dataCount];
                    double[] intensities = new double[dataCount];

                    // 【注意】：EPPlus 读取单个单元格的性能尚可接受，无需逆向使用矩阵
                    for (int i = 0; i < dataCount; i++)
                    {
                        int row = i + 6;
                        double.TryParse(sheet.Cells[row, 1].Text, out wavelengths[i]);
                        double.TryParse(sheet.Cells[row, 2].Text, out intensities[i]);
                    }

                    return new SpectralData
                    {
                        Wavelengths = wavelengths,
                        Intensities = intensities,
                        AcquisitionTime = acqTime,
                        SourceDeviceSerial = "Reference" // 特殊魔术字符串：告知 UI 这是一个固态的参考图层
                    };
                }
                catch
                {
                    // 解析失败容错拦截
                    return null;
                }
            });
        }

        #endregion

        #region 5. 资源回收

        public void Dispose()
        {
            EndSession(); // 确保垃圾回收时主动释放文件锁
        }

        #endregion
    }
}