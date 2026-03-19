using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class TimeSeriesViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RecordText))]
        [NotifyPropertyChangedFor(nameof(RecordIcon))]
        [NotifyPropertyChangedFor(nameof(RecordButtonColor))]
        private bool _isRecording;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ViewModeText))]
        [NotifyPropertyChangedFor(nameof(ViewModeIcon))]
        private bool _isMatrixView;

        [ObservableProperty]
        private int _gridColumns = 1;

        private readonly JsonConfigService _configService;
        private readonly PeakTrackingService _peakTrackingService;

        [ObservableProperty]
        private ObservableCollection<TimeSeriesChartItem> _charts = new();

        private readonly Stopwatch _stopwatch = new();

        private const int MAX_UI_POINTS = 10000;

        // ==========================================
        // 【新增】：后台数据保存缓存与锁
        // ==========================================
        private readonly List<Models.Spectrometer.TimeSeriesSnapshot> _timeSeriesCache = new();
        private readonly List<string> _lockedTrackPointNames = new();
        private readonly object _cacheLock = new object();

        // 【新增】：用于缓存最新收到的硬件参数
        private float _currentIntegrationTime;
        private uint _currentAveragingCount;

        public string RecordText => IsRecording ? "停止记录" : "开始记录";
        public string RecordIcon => IsRecording ? "StopCircle" : "PlayCircle";
        public string RecordButtonColor => IsRecording ? "#f44336" : "#673ab7";
        public string ViewModeText => IsMatrixView ? "列表视图" : "矩阵视图";
        public string ViewModeIcon => IsMatrixView ? "ViewSequential" : "ViewGrid";

        public TimeSeriesViewModel(JsonConfigService configService, PeakTrackingService peakTrackingService)
        {
            _configService = configService;
            _peakTrackingService = peakTrackingService;

            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                // 【新增】：每次收到新光谱，顺手更新一下当前的硬件参数
                _currentIntegrationTime = m.IntegrationTime;
                _currentAveragingCount = m.AveragingCount;
                if (IsRecording) InjectDataToCharts();
            });

            ReloadChartsFromConfig();
        }

        partial void OnIsMatrixViewChanged(bool value) => UpdateGridColumns();

        private void UpdateGridColumns()
        {
            GridColumns = (IsMatrixView && Charts.Count > 1) ? 2 : 1;
        }

        private void ReloadChartsFromConfig()
        {
            var config = _configService.Load();
            Charts.Clear();

            var validNodes = config.TimeSeriesSampleNodes?
                .Where(n => !string.IsNullOrWhiteSpace(n.Name) && n.SelectedPeakX.HasValue)
                .Where(n => _peakTrackingService.TrackedPeaks.Any(p => Math.Abs(p.BaseWavelength - n.SelectedPeakX!.Value) < 0.01))
                .ToList();

            if (validNodes == null || validNodes.Count == 0)
            {
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

        private void InjectDataToCharts()
        {
            double currentTime = _stopwatch.Elapsed.TotalSeconds;

            // 用于后台落盘的强度数组缓存
            double[] currentIntensities = new double[Charts.Count];

            // 【新增】：记录当前存活的有效峰线数量
            int activePeakCount = 0;

            for (int i = 0; i < Charts.Count; i++)
            {
                var chart = Charts[i];
                var matchedPeak = _peakTrackingService.TrackedPeaks
                    .FirstOrDefault(p => Math.Abs(p.BaseWavelength - chart.TargetWavelength) < 0.01);

                if (matchedPeak != null)
                {
                    // 记录这一帧该波长的强度
                    currentIntensities[i] = matchedPeak.CurrentIntensity;
                    activePeakCount++; // 发现有效峰线，计数+1

                    lock (chart.SyncRoot)
                    {
                        chart.TimePoints.Add(currentTime);
                        chart.IntensityPoints.Add(matchedPeak.CurrentIntensity);

                        // 滚动窗口逻辑
                        if (chart.TimePoints.Count > MAX_UI_POINTS)
                        {
                            chart.TimePoints.RemoveAt(0);
                            chart.IntensityPoints.RemoveAt(0);
                        }

                        chart.HasNewData = true;
                    }
                }
            }

            // 【新增】：如果所有峰线都消失了，自动停止记录
            if (activePeakCount == 0)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (IsRecording)
                    {
                        // 触发停止并保存的命令
                        _ = ToggleRecordingAsync();
                    }
                });
                return; // 直接返回，不保存这帧全 0 的废数据
            }

            // 将提取出的强度数组切片，压入内存队列
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
        /// 紧急静默保存 (专供软件关闭时调用)
        /// </summary>
        public void EmergencySave()
        {
            // 如果没在录制，或者缓存没数据，直接返回
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

            // 文件名加个 _Exit 后缀，代表这是退出时抢救下来的数据
            string fileName = $"TimeSeries_Auto_{DateTime.Now:yyyyMMdd_HHmmss}_Exit.xlsx";
            string fullPath = Path.Combine(dirPath, fileName);

            using var exportService = new ExcelExportService();
            // 【核心】：使用 .GetAwaiter().GetResult() 强制同步阻塞，且不涉及任何 UI 弹窗
            exportService.ExportTimeSeriesBulkAsync(
                fullPath,
                columnsToSave,
                dataToSave,
                _currentIntegrationTime,
                _currentAveragingCount).GetAwaiter().GetResult();
        }

        // 【修改】：将原来的 void ToggleRecording 改为 async Task 异步命令
        [RelayCommand]
        private async Task ToggleRecordingAsync()
        {
            if (IsRecording)
            {
                // ==========================================
                // 停止记录并落盘 (原有逻辑保持不变)
                // ==========================================
                IsRecording = false;
                _stopwatch.Stop();

                List<Models.Spectrometer.TimeSeriesSnapshot> dataToSave;
                List<string> columnsToSave;

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
                        fullPath,
                        columnsToSave,
                        dataToSave,
                        _currentIntegrationTime,
                        _currentAveragingCount);

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
                // 开始记录并初始化缓存
                // ==========================================

                // 【新增】：检查当前是否还有有效的追踪峰线，没有则拦截启动
                int activePeakCount = Charts.Count(chart =>
                    _peakTrackingService.TrackedPeaks.Any(p => Math.Abs(p.BaseWavelength - chart.TargetWavelength) < 0.01));

                if (activePeakCount == 0)
                {
                    MessageBox.Show("当前主界面没有正在追踪的有效峰线，无法启动记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 锁定表头（当前追踪的各个采样点名称）
                lock (_cacheLock)
                {
                    _timeSeriesCache.Clear();
                    _lockedTrackPointNames.Clear();
                    _lockedTrackPointNames.AddRange(Charts.Select(c => c.Title));
                }

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
            ReloadChartsFromConfig();
        }

        public partial class TimeSeriesChartItem : ObservableObject
        {
            [ObservableProperty] private string _title;
            [ObservableProperty] private double _targetWavelength;

            public List<double> TimePoints { get; } = new();
            public List<double> IntensityPoints { get; } = new();

            public object SyncRoot { get; } = new object();

            public ScottPlot.Color LineColor { get; }
            public bool HasNewData { get; set; } = false;

            public TimeSeriesChartItem(string title, double targetWavelength, int colorIndex)
            {
                Title = title;
                TargetWavelength = targetWavelength;

                string[] hexColors = { "#1976D2", "#D32F2F", "#388E3C", "#F57C00", "#7B1FA2", "#0097A7", "#E64A19", "#689F38", "#C2185B", "#5D4037" };
                LineColor = ScottPlot.Color.FromHex(hexColors[colorIndex % hexColors.Length]);
            }
        }
    }
}