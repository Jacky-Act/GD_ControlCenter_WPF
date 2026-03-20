using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

/*
 * 文件名: TimeSeriesViewModel.cs
 * 模块: 视图模型层 (UI ViewModels)
 * 描述: 多通道时序滚动图表的数据中枢。
 * 架构职责:
 * 1. 【双轨数据流】：一方面维持少量数据供前端 UI 50ms 刷新（FIFO淘汰制），另一方面将海量数据压入后台无限内存池（追加制）。
 * 2. 【并发安全】：通过 _cacheLock 和内部的 SyncRoot，确保底层 C++ 高频回调写入与 UI 定时器读取绝对互不干扰。
 */

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class TimeSeriesViewModel : ObservableObject
    {
        #region 1. 核心依赖与 UI 状态绑定 (Dependencies & UI State)

        /// <summary>
        /// 本地 JSON 配置服务，用于读取用户关注的特征峰参数。
        /// </summary>
        private readonly JsonConfigService _configService;

        /// <summary>
        /// 全局寻峰服务。通过对比此服务中追踪的实时波长，来提取对应强度。
        /// </summary>
        private readonly PeakTrackingService _peakTrackingService;

        /// <summary>
        /// 核心状态：当前是否正在执行后台的连续时序记录。
        /// 它的变化会自动级联触发按钮文字、图标和颜色的改变。
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RecordText))]
        [NotifyPropertyChangedFor(nameof(RecordIcon))]
        [NotifyPropertyChangedFor(nameof(RecordButtonColor))]
        private bool _isRecording;

        /// <summary>
        /// 核心状态：时序图的布局模式。True = 并排矩阵模式，False = 上下列表模式。
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ViewModeText))]
        [NotifyPropertyChangedFor(nameof(ViewModeIcon))]
        private bool _isMatrixView;

        /// <summary>
        /// 前端 UniformGrid 绑定的动态列数。根据 _isMatrixView 和图表总数动态计算。
        /// </summary>
        [ObservableProperty]
        private int _gridColumns = 1;

        /// <summary>
        /// 独立图表视图模型的集合。每个元素代表 UI 上的一个子图表通道。
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<TimeSeriesChartItem> _charts = new();

        /// <summary>
        /// 高精度计时器。用于为每一帧时序数据打上精确的相对时间戳 (X 轴坐标)。
        /// </summary>
        private readonly Stopwatch _stopwatch = new();

        /// <summary>
        /// 常量：UI 画布允许滞留的最大点数。超过此数值将触发先进先出(FIFO)淘汰，防止前端内存爆炸。
        /// （注：此限制仅针对 UI 渲染，后台 Excel 导出的缓存不受此限制）
        /// </summary>
        private const int MAX_UI_POINTS = 10000;

        // --- 动态级联 UI 属性 ---
        public string RecordText => IsRecording ? "停止记录" : "开始记录";
        public string RecordIcon => IsRecording ? "StopCircle" : "PlayCircle";
        public string RecordButtonColor => IsRecording ? "#f44336" : "#673ab7";
        public string ViewModeText => IsMatrixView ? "列表视图" : "矩阵视图";
        public string ViewModeIcon => IsMatrixView ? "ViewSequential" : "ViewGrid";

        #endregion

        #region 2. 多线程缓存池与锁基建 (Thread-Safe Cache Pool)

        /// <summary>
        /// 核心并发锁。保护后台无限缓存池 _timeSeriesCache 和 _lockedTrackPointNames，
        /// 防止在“停止落盘”的瞬间，底层还在疯狂往里塞数据导致集合异常。
        /// </summary>
        private readonly object _cacheLock = new object();

        /// <summary>
        /// 后台无限缓存池。在录制期间，所有被提取的有效强度快照都会被追加到这里，直至最终导出为 Excel。
        /// </summary>
        private readonly List<Models.Spectrometer.TimeSeriesSnapshot> _timeSeriesCache = new();

        /// <summary>
        /// 记录启动瞬间锁定的表头列名。
        /// 防止用户在录制途中修改配置导致 Excel 列数错乱。
        /// </summary>
        private readonly List<string> _lockedTrackPointNames = new();

        #endregion

        #region 3. 硬件上下文快照 (Hardware Context)

        /// <summary>
        /// 缓存最新收到数据的积分时间快照（用于最终写进 Excel 表头）。
        /// </summary>
        private float _currentIntegrationTime;

        /// <summary>
        /// 缓存最新收到数据的平均次数快照（用于最终写进 Excel 表头）。
        /// </summary>
        private uint _currentAveragingCount;

        #endregion

        #region 4. 初始化与消息挂载

        public TimeSeriesViewModel(JsonConfigService configService, PeakTrackingService peakTrackingService)
        {
            _configService = configService;
            _peakTrackingService = peakTrackingService;

            // 注册对底层硬件高频数据的“零阻塞”拦截
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                // 1. 顺手更新硬件上下文参数
                _currentIntegrationTime = m.IntegrationTime;
                _currentAveragingCount = m.AveragingCount;

                // 2. 如果正在录制，则将数据泵入双轨流水线
                if (IsRecording) InjectDataToCharts();
            });

            // 启动时读取本地配置生成初始通道图表
            ReloadChartsFromConfig();
        }

        #endregion

        #region 5. 核心业务流水线 (数据泵与启停机)

        /// <summary>
        /// 数据泵引擎：在接收到底层新一帧数据时，将其拆解并泵入对应的 UI 图表和后台缓存。
        /// </summary>
        private void InjectDataToCharts()
        {
            double currentTime = _stopwatch.Elapsed.TotalSeconds;

            // 预分配数组：用于打包当前所有通道的瞬时强度，准备压入后台缓存池
            double[] currentIntensities = new double[Charts.Count];

            int activePeakCount = 0; // 存活的有效峰线计数器

            // --- 步骤 1：遍历并提取数据 ---
            for (int i = 0; i < Charts.Count; i++)
            {
                var chart = Charts[i];
                var matchedPeak = _peakTrackingService.TrackedPeaks
                    .FirstOrDefault(p => Math.Abs(p.BaseWavelength - chart.TargetWavelength) < 0.01);

                if (matchedPeak != null)
                {
                    currentIntensities[i] = matchedPeak.CurrentIntensity;
                    activePeakCount++;

                    // --- 步骤 2：更新 UI 渲染队列 (带并发保护) ---
                    // 锁定单个 ChartItem 的小锁，防止前端画图定时器在此瞬间读取到残缺数据
                    lock (chart.SyncRoot)
                    {
                        chart.TimePoints.Add(currentTime);
                        chart.IntensityPoints.Add(matchedPeak.CurrentIntensity);

                        // FIFO 滚动剔除：维持前端画布内存健康
                        if (chart.TimePoints.Count > MAX_UI_POINTS)
                        {
                            chart.TimePoints.RemoveAt(0);
                            chart.IntensityPoints.RemoveAt(0);
                        }

                        chart.HasNewData = true; // 挂上脏标记，通知前端可以重绘了
                    }
                }
            }

            // --- 步骤 3：防呆拦截 (异常停机) ---
            // 如果用户在主界面把所有追踪峰都删了，直接强行停止录制并保存，不存全 0 废数据
            if (activePeakCount == 0)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (IsRecording) _ = ToggleRecordingAsync();
                });
                return;
            }

            // --- 步骤 4：追加后台缓存 (带并发保护) ---
            lock (_cacheLock)
            {
                _timeSeriesCache.Add(new Models.Spectrometer.TimeSeriesSnapshot
                {
                    Timestamp = DateTime.Now,
                    Intensities = currentIntensities
                });
            }
        }

        /// <summary>
        /// 异步启停状态机：控制时序图的开始记录、清空与落盘导出。
        /// </summary>
        [RelayCommand]
        private async Task ToggleRecordingAsync()
        {
            if (IsRecording)
            {
                // ==========================================
                // 流水线 A：停止记录并安全落盘
                // ==========================================
                IsRecording = false;
                _stopwatch.Stop();

                List<Models.Spectrometer.TimeSeriesSnapshot> dataToSave;
                List<string> columnsToSave;

                // 【瞬间剪切操作】：锁定总锁，将所有缓存数据快速转移到局部变量中，随后立即释放。
                // 这样后续耗时好几秒的写硬盘操作就不会阻塞业务逻辑。
                lock (_cacheLock)
                {
                    dataToSave = _timeSeriesCache.ToList();
                    columnsToSave = _lockedTrackPointNames.ToList();
                    _timeSeriesCache.Clear();
                    _lockedTrackPointNames.Clear();
                }

                if (dataToSave.Count > 0)
                {
                    var config = _configService.Load();
                    string dirPath = string.IsNullOrWhiteSpace(config.TimeSeriesSaveDirectory)
                        ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                        : config.TimeSeriesSaveDirectory;

                    string fileName = $"TimeSeries_Auto_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    string fullPath = Path.Combine(dirPath, fileName);

                    using var exportService = new ExcelExportService();

                    bool success = await exportService.ExportTimeSeriesBulkAsync(
                        fullPath, columnsToSave, dataToSave, _currentIntegrationTime, _currentAveragingCount);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (success)
                            MessageBox.Show($"时序图已停止记录。\n数据已自动保存至：\n{fullPath}", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        else
                            MessageBox.Show("时序图自动保存失败，目标文件可能被占用。", "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            }
            else
            {
                // ==========================================
                // 流水线 B：开始记录并初始化缓存
                // ==========================================

                // 检查当前是否还有有效的追踪峰线，没有则拒绝启动
                int activePeakCount = Charts.Count(chart =>
                    _peakTrackingService.TrackedPeaks.Any(p => Math.Abs(p.BaseWavelength - chart.TargetWavelength) < 0.01));

                if (activePeakCount == 0)
                {
                    MessageBox.Show("当前主界面没有正在追踪的有效峰线，无法启动记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 锁定表头列名
                lock (_cacheLock)
                {
                    _timeSeriesCache.Clear();
                    _lockedTrackPointNames.Clear();
                    _lockedTrackPointNames.AddRange(Charts.Select(c => c.Title));
                }

                // 重置所有前端画布的数据点
                foreach (var chart in Charts)
                {
                    lock (chart.SyncRoot)
                    {
                        chart.TimePoints.Clear();
                        chart.IntensityPoints.Clear();
                        chart.HasNewData = true;
                    }
                }

                _stopwatch.Restart();
                IsRecording = true;
            }
        }

        /// <summary>
        /// 紧急静默保存 (专供软件意外被强杀时在 App.xaml.cs 中调用)
        /// </summary>
        public void EmergencySave()
        {
            if (!IsRecording || _timeSeriesCache.Count == 0) return;

            IsRecording = false;
            _stopwatch.Stop();

            List<Models.Spectrometer.TimeSeriesSnapshot> dataToSave;
            List<string> columnsToSave;

            lock (_cacheLock)
            {
                dataToSave = _timeSeriesCache.ToList();
                columnsToSave = _lockedTrackPointNames.ToList();
                _timeSeriesCache.Clear();
            }

            var config = _configService.Load();
            string dirPath = string.IsNullOrWhiteSpace(config.TimeSeriesSaveDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : config.TimeSeriesSaveDirectory;

            string fileName = $"TimeSeries_Auto_{DateTime.Now:yyyyMMdd_HHmmss}_Exit.xlsx";
            string fullPath = Path.Combine(dirPath, fileName);

            using var exportService = new ExcelExportService();
            // 【核心安全策略】：使用 .GetAwaiter().GetResult() 强制同步阻塞让主线程死等，不触发任何 UI 更新和弹窗
            exportService.ExportTimeSeriesBulkAsync(
                fullPath, columnsToSave, dataToSave, _currentIntegrationTime, _currentAveragingCount).GetAwaiter().GetResult();
        }

        #endregion

        #region 6. UI 控制与配置刷新 (UI Commands)

        partial void OnIsMatrixViewChanged(bool value) => UpdateGridColumns();

        private void UpdateGridColumns()
        {
            GridColumns = (IsMatrixView && Charts.Count > 1) ? 2 : 1;
        }

        [RelayCommand]
        private void ToggleViewMode() => IsMatrixView = !IsMatrixView;

        [RelayCommand]
        private void OpenSamplingConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.TimeSeriesSamplingConfigWindow();
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.TimeSeriesSamplingConfigViewModel(_configService, _peakTrackingService, () => window.Close());
            window.DataContext = vm;
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();

            // 弹窗关闭后，根据用户的新配置重新装载图表
            ReloadChartsFromConfig();
        }

        /// <summary>
        /// 从本地配置文件中重新构建通道图表集合。
        /// </summary>
        private void ReloadChartsFromConfig()
        {
            var config = _configService.Load();
            Charts.Clear();

            // 过滤出合法、且当前主界面依然在追踪的节点
            var validNodes = config.TimeSeriesSampleNodes?
                .Where(n => !string.IsNullOrWhiteSpace(n.Name) && n.SelectedPeakX.HasValue)
                .Where(n => _peakTrackingService.TrackedPeaks.Any(p => Math.Abs(p.BaseWavelength - n.SelectedPeakX!.Value) < 0.01))
                .ToList();

            if (validNodes == null || validNodes.Count == 0)
            {
                // 空状态兜底
                Charts.Add(new TimeSeriesChartItem("Null", 0, 0));
            }
            else
            {
                for (int i = 0; i < validNodes.Count; i++)
                {
                    var node = validNodes[i];
                    Charts.Add(new TimeSeriesChartItem($"{node.Name} ({node.SelectedPeakX!.Value:F2} nm)", node.SelectedPeakX!.Value, i));
                }
            }
            UpdateGridColumns();
        }

        #endregion

        #region 7. 嵌套类：子图表数据承载体

        /// <summary>
        /// 单个通道的时序图表数据模型。
        /// 职责：持有一条折线图的所有点集信息，并提供独立的并发锁防止脏读。
        /// </summary>
        public partial class TimeSeriesChartItem : ObservableObject
        {
            [ObservableProperty] private string _title;
            [ObservableProperty] private double _targetWavelength;

            public List<double> TimePoints { get; } = new();
            public List<double> IntensityPoints { get; } = new();

            /// <summary>
            /// 细粒度并发锁。
            /// 因为数据泵在后台线程不断往 Lists 里 Add 数据，而前台 View 的 50ms 定时器在主线程读取。
            /// 必须用此锁包裹所有对 TimePoints 和 IntensityPoints 的操作，防止抛出“集合已修改”异常。
            /// </summary>
            public object SyncRoot { get; } = new object();

            /// <summary>
            /// 画布线条颜色。由构造时传入的索引自动从色板中取色。
            /// </summary>
            public ScottPlot.Color LineColor { get; }

            /// <summary>
            /// 脏数据标记：指示自上次渲染以来，是否有新的底层数据加入。
            /// 供 View 层的定时器检查以决定是否耗费 CPU 资源去重绘。
            /// </summary>
            public bool HasNewData { get; set; } = false;

            public TimeSeriesChartItem(string title, double targetWavelength, int colorIndex)
            {
                Title = title;
                TargetWavelength = targetWavelength;

                // 预设高对比度科学绘图色板
                string[] hexColors = { "#1976D2", "#D32F2F", "#388E3C", "#F57C00", "#7B1FA2", "#0097A7", "#E64A19", "#689F38", "#C2185B", "#5D4037" };
                LineColor = ScottPlot.Color.FromHex(hexColors[colorIndex % hexColors.Length]);
            }
        }

        #endregion
    }
}