using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using Microsoft.Extensions.DependencyInjection;
using ScottPlot;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class ControlPanelView : UserControl
    {
        private readonly string _primaryColorHex = "#673ab7";
        private readonly PeakTrackingService _peakTrackingService;

        private bool _isFirstFrame = true;
        private DateTime _lastRenderTime = DateTime.MinValue;
        private AxisLimits? _maxLimits = null;
        private double _lastRightClickX = 0;
        private SpectralData? _currentData;
        private bool _requestAutoScale = false;

        // 【新增】：缓存参考文件的数据，而不是图层对象。这样就不怕被 Clear() 洗掉了
        private SpectralData? _cachedReferenceData = null;

        public ControlPanelView()
        {
            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;
            _peakTrackingService = App.Services.GetRequiredService<PeakTrackingService>();
            SpecPlot.PreviewMouseRightButtonDown += OnPlotRightClick;
            SetupCustomMenu();

            // 【核心修复3】：全局设置图表的边距。X轴边距设为 0（绝对贴边），Y轴上下留白 10%。
            // 这样设置后，不论是双击滚轮还是代码触发 AutoScale，X 轴永远完美贴合左右！
            SpecPlot.Plot.Axes.Margins(0, 0.1);
        }

        private void SetupCustomMenu()
        {
            SpecPlot.Menu?.Clear();
            SpecPlot.Menu?.Add("寻峰", (plotControl) =>
            {
                if (_currentData != null)
                {
                    double captureWindow = CalculateToleranceByPixels(20, 0.5);
                    double snappedX = SpectrometerLogic.GetActualPeakWavelength(_currentData, _lastRightClickX, captureWindow);
                    _peakTrackingService.AddPeak(snappedX);
                    RenderPlot(_currentData);
                }
            });
            SpecPlot.Menu?.Add("去除附近一条", (plotControl) =>
            {
                if (_currentData != null)
                {
                    double removeTolerance = CalculateToleranceByPixels(50, 5.0);
                    _peakTrackingService.RemovePeakNear(_lastRightClickX, removeTolerance);
                    RenderPlot(_currentData);
                }
            });
            SpecPlot.Menu?.Add("去除全部", (plotControl) =>
            {
                _peakTrackingService.ClearAll();
                if (_currentData != null) RenderPlot(_currentData);
                else SpecPlot.Refresh();
            });
        }

        private void OnPlotRightClick(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(SpecPlot);
            double dpiScaleX = 1.0, dpiScaleY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(SpecPlot);
            if (source != null && source.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            float physicalX = (float)(pos.X * dpiScaleX);
            float physicalY = (float)(pos.Y * dpiScaleY);
            var coordinates = SpecPlot.Plot.GetCoordinates(physicalX, physicalY);
            _lastRightClickX = coordinates.X;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);

            // 监听：硬件新光谱数据
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                OnPlotUpdateRequested(m.Value);
            });

            // 监听：加载参考文件
            WeakReferenceMessenger.Default.Register<LoadReferencePlotMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _cachedReferenceData = m.Value; // 缓存数据

                    // 【修复】：无论有没有实时最后一帧，强制重绘当前手中的所有数据
                    RenderPlot(_currentData);

                    // 如果当前没有实时数据，则针对参考文件进行一次坐标轴自适应
                    if (_currentData == null)
                    {
                        SpecPlot.Plot.Axes.AutoScale();
                        SpecPlot.Refresh();
                    }
                });
            });

            // 监听：关闭参考文件
            WeakReferenceMessenger.Default.Register<ClearReferencePlotMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _cachedReferenceData = null; // 清空缓存

                    // 【修复】：直接重绘。如果 _currentData 也是 null，RenderPlot 内部会自动清空画布
                    RenderPlot(_currentData);
                });
            });

            // 【修复】：“自动范围”按钮联动
            WeakReferenceMessenger.Default.Register<AutoRangeRequestMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _requestAutoScale = true;
                    if (_currentData != null)
                    {
                        RenderPlot(_currentData);
                    }
                    else if (_cachedReferenceData != null)
                    {
                        // 核心：如果只有参考图，先清除所有坐标限制规则，再强制自适应
                        SpecPlot.Plot.Axes.Rules.Clear();
                        SpecPlot.Plot.Axes.AutoScale();
                        SpecPlot.Refresh();
                    }
                });
            });

            // 【修复】：“最大范围”按钮联动
            WeakReferenceMessenger.Default.Register<MaxRangeRequestMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_maxLimits.HasValue)
                    {
                        SpecPlot.Plot.Axes.SetLimits(_maxLimits.Value);
                    }
                    else if (_cachedReferenceData != null)
                    {
                        // 核心：如果硬件没跑过，最大范围就以“参考文件”的波长首尾为基准
                        double minX = _cachedReferenceData.Wavelengths[0];
                        double maxX = _cachedReferenceData.Wavelengths[^1];

                        // Y轴给一个宽泛的默认范围 (比如 -1000 到 65535)
                        SpecPlot.Plot.Axes.SetLimits(minX, maxX, -1000, 65535);
                    }
                    SpecPlot.Refresh();
                });
            });

            // 【核心修复】：当 View 重新创建并绑定 DataContext 时，主动向 ViewModel 索取历史状态
            if (e.NewValue is GD_ControlCenter_WPF.ViewModels.ControlPanelViewModel vm)
            {
                bool needRefresh = false;

                // 1. 恢复参考文件数据
                if (vm.IsReferenceFileLoaded && vm.LoadedReferenceData != null)
                {
                    _cachedReferenceData = vm.LoadedReferenceData;
                    needRefresh = true;
                }

                // 2. 恢复最后一帧实时数据 (解决：光谱仪停止状态下切页面，最后一帧也会消失的问题)
                if (vm.LatestSpectralData != null)
                {
                    _currentData = vm.LatestSpectralData;
                    needRefresh = true;
                }

                // 3. 如果拿到了任何缓存数据，立即强制重绘
                if (needRefresh)
                {
                    Dispatcher.Invoke(() =>
                    {
                        RenderPlot(_currentData);

                        // 如果只有参考文件而没有实时数据，确保触发一次坐标自适应，防止画面缩在原点
                        if (_currentData == null)
                        {
                            SpecPlot.Plot.Axes.AutoScale();
                            SpecPlot.Refresh();
                        }
                    });
                }
            }
        }

        private void OnPlotUpdateRequested(SpectralData data)
        {
            if ((DateTime.Now - _lastRenderTime).TotalMilliseconds < 33) return;
            _lastRenderTime = DateTime.Now;
            Dispatcher.BeginInvoke(new Action(() => RenderPlot(data)));
        }

        /// <summary>
        /// 核心绘制方法：支持同时渲染实时数据和参考缓存数据
        /// </summary>
        private void RenderPlot(SpectralData? liveData)
        {
            // 每次渲染前依然清空画布，防止内存泄漏和峰值文本重叠
            SpecPlot.Plot.Clear();

            // ==========================================
            // 图层 1：先画参考文件（红色虚线，被实时线覆盖在下方）
            // ==========================================
            if (_cachedReferenceData != null)
            {
                var refPlot = SpecPlot.Plot.Add.Scatter(_cachedReferenceData.Wavelengths, _cachedReferenceData.Intensities);
                refPlot.MarkerSize = 0;
                refPlot.LineWidth = 1.0f;
                refPlot.Color = ScottPlot.Color.FromHex("#F44336"); // 红色

                // 【核心修复4】：不要设置 LegendText，也不调用 ShowLegend()，彻底去掉图例。
            }

            // ==========================================
            // 图层 2：再画实时硬件光谱（紫色实线）
            // ==========================================
            if (liveData != null && liveData.Wavelengths != null && liveData.Wavelengths.Length > 0)
            {
                _currentData = liveData;
                var livePlot = SpecPlot.Plot.Add.Scatter(liveData.Wavelengths, liveData.Intensities);
                livePlot.MarkerSize = 0;
                livePlot.LineWidth = 1.5f;
                livePlot.Color = ScottPlot.Color.FromHex(_primaryColorHex);

                HandleScalingLogic(liveData);

                var currentLimits = SpecPlot.Plot.Axes.GetLimits();
                DrawTrackedPeaks(currentLimits.Top);
            }

            SpecPlot.Refresh();
        }

        private void HandleScalingLogic(SpectralData data)
        {
            if (_isFirstFrame)
            {
                double minWavelength = data.Wavelengths[0];
                double maxWavelength = data.Wavelengths[^1];

                _maxLimits = new AxisLimits(minWavelength, maxWavelength, -1000, 65535);
                var limitRule = new ScottPlot.AxisRules.MaximumBoundary(
                    SpecPlot.Plot.Axes.Bottom,
                    SpecPlot.Plot.Axes.Left,
                    _maxLimits.Value
                );

                SpecPlot.Plot.Axes.Rules.Clear();
                SpecPlot.Plot.Axes.Rules.Add(limitRule);

                SpecPlot.Plot.Axes.AutoScale();
                _isFirstFrame = false;
            }
            else if (_requestAutoScale)
            {
                SpecPlot.Plot.Axes.AutoScale();
                _requestAutoScale = false;
            }
        }

        private void DrawTrackedPeaks(double yAxisMax)
        {
            foreach (var peak in _peakTrackingService.TrackedPeaks)
            {
                var vLine = SpecPlot.Plot.Add.VerticalLine(peak.CurrentWavelength);
                vLine.Color = ScottPlot.Color.FromHex("#F44336");
                vLine.LineWidth = 1.0f;
                vLine.LinePattern = ScottPlot.LinePattern.Solid;

                var txt = SpecPlot.Plot.Add.Text($"X: {peak.CurrentWavelength:F2}\nY: {peak.CurrentIntensity:F0}",
                                                 peak.CurrentWavelength,
                                                 yAxisMax);

                txt.LabelFontColor = ScottPlot.Color.FromHex("#F44336");
                txt.LabelFontSize = 14;
                txt.LabelBold = true;
                txt.LabelAlignment = ScottPlot.Alignment.UpperCenter;
            }
        }

        private double CalculateToleranceByPixels(int pixelCount, double minTolerance)
        {
            double xAxisSpan = SpecPlot.Plot.Axes.Bottom.Range.Span;
            double plotWidth = SpecPlot.ActualWidth;
            double nmPerPixel = plotWidth > 0 ? xAxisSpan / plotWidth : 0.1;
            return Math.Max(nmPerPixel * pixelCount, minTolerance);
        }
    }
}