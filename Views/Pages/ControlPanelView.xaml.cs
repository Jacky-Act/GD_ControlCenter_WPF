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

/*
 * 文件名: ControlPanelView.xaml.cs
 * 模块: 视图层 (UI Code-Behind)
 * 描述: 主控制面板的图表渲染引擎。
 * 架构职责: 
 * 1. 绝对不包含任何业务逻辑。只负责监听 ViewModel 广播的数据消息，然后把点画在屏幕上。
 * 2. 【双图层渲染】：负责协调“红色静态参考波形”与“紫色动态实时波形”的叠加显示。
 * 3. 【坐标系接管】：处理用户双击滚轮、框选放大以及代码触发的 AutoScale 坐标轴自适应。
 */

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class ControlPanelView : UserControl
    {
        #region 1. 渲染状态与缓存变量

        /// <summary>
        /// 实时波形的主题色（紫色）。
        /// </summary>
        private readonly string _primaryColorHex = "#673ab7";

        /// <summary>
        /// 寻峰服务。这里破例引入 Service，仅仅是为了在用户右键图表时，能把点击坐标传给后台算法。
        /// </summary>
        private readonly PeakTrackingService _peakTrackingService;

        /// <summary>
        /// 坐标系初始化标志。第一帧数据到来时，需要给 X/Y 轴定下基础边界规则。
        /// </summary>
        private bool _isFirstFrame = true;

        /// <summary>
        /// 渲染节流阀的时间戳记录。限制最高刷新率为 30FPS（~33ms），防止 GPU 满载。
        /// </summary>
        private DateTime _lastRenderTime = DateTime.MinValue;

        /// <summary>
        /// 全局最大视野边界限制。防止用户缩小图表时看到无意义的空白区域。
        /// </summary>
        private AxisLimits? _maxLimits = null;

        /// <summary>
        /// 记录用户在图表上点击右键时的真实物理波长坐标 (X轴)。
        /// </summary>
        private double _lastRightClickX = 0;

        /// <summary>
        /// 请求标志：由“自动范围”按钮触发，通知下一次 RenderPlot 时强制执行一次 AutoScale。
        /// </summary>
        private bool _requestAutoScale = false;

        /// <summary>
        /// 当前手中握有的最新一帧实时硬件光谱数据。
        /// </summary>
        private SpectralData? _currentData;

        /// <summary>
        /// 当前载入的参考文件光谱数据（底层图层）。
        /// 必须缓存数据本身，而不是图层对象，以防止图表 Clear() 时被误删。
        /// </summary>
        private SpectralData? _cachedReferenceData = null;

        #endregion

        #region 2. 初始化与 UI 事件订阅

        public ControlPanelView()
        {
            InitializeComponent();

            // 监听 DataContext 的绑定变化，用于注册消息和恢复状态
            this.DataContextChanged += OnDataContextChanged;

            _peakTrackingService = App.Services.GetRequiredService<PeakTrackingService>();

            // 订阅图表右键事件，用于弹出“寻峰”菜单
            SpecPlot.PreviewMouseRightButtonDown += OnPlotRightClick;
            SetupCustomMenu();

            // 【核心视觉优化】：X 轴边距设为 0（绝对贴边），Y 轴上下留白 10%。
            // 这样用户双击滚轮自适应时，曲线会完美顶满左右两边，不会留有丑陋的白边。
            SpecPlot.Plot.Axes.Margins(0, 0.1);
        }

        /// <summary>
        /// 构建图表右键弹出的寻峰菜单逻辑。
        /// </summary>
        private void SetupCustomMenu()
        {
            SpecPlot.Menu?.Clear();

            SpecPlot.Menu?.Add("寻峰", (plotControl) =>
            {
                if (_currentData != null)
                {
                    // 根据当前屏幕缩放比例，动态计算一个合理的鼠标点击宽容度 (20像素)
                    double captureWindow = CalculateToleranceByPixels(20, 0.5);
                    // 交给算法去纠正到真正的物理峰值
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

            // 将鼠标像素坐标转换为图表内部的真实波长坐标
            var coordinates = SpecPlot.Plot.GetCoordinates(physicalX, physicalY);
            _lastRightClickX = coordinates.X;
        }

        #endregion

        #region 3. MVVM 消息总线接管 (数据获取)

        /// <summary>
        /// 当页面的 ViewModel 绑定完成时，开启对全局消息总线的监听。
        /// </summary>
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);

            // 监听：硬件产生的新光谱数据
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                OnPlotUpdateRequested(m.Value);
            });

            // 监听：ViewModel 要求加载参考文件
            WeakReferenceMessenger.Default.Register<LoadReferencePlotMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _cachedReferenceData = m.Value;

                    // 无论有没有实时数据，强制重绘当前手中的所有图层
                    RenderPlot(_currentData);

                    // 如果当前硬件没开，针对刚刚加载的参考文件进行一次坐标轴自适应，防止画面空空如也
                    if (_currentData == null)
                    {
                        SpecPlot.Plot.Axes.AutoScale();
                        SpecPlot.Refresh();
                    }
                });
            });

            // 监听：ViewModel 要求关闭参考文件
            WeakReferenceMessenger.Default.Register<ClearReferencePlotMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _cachedReferenceData = null;
                    RenderPlot(_currentData);
                });
            });

            // 监听：用户点击“自动范围”按钮
            WeakReferenceMessenger.Default.Register<AutoRangeRequestMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _requestAutoScale = true;
                    if (_currentData != null) RenderPlot(_currentData);
                    else if (_cachedReferenceData != null)
                    {
                        SpecPlot.Plot.Axes.Rules.Clear();
                        SpecPlot.Plot.Axes.AutoScale();
                        SpecPlot.Refresh();
                    }
                });
            });

            // 监听：用户点击“最大范围”按钮
            WeakReferenceMessenger.Default.Register<MaxRangeRequestMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_maxLimits.HasValue) SpecPlot.Plot.Axes.SetLimits(_maxLimits.Value);
                    else if (_cachedReferenceData != null)
                    {
                        double minX = _cachedReferenceData.Wavelengths[0];
                        double maxX = _cachedReferenceData.Wavelengths[^1];
                        SpecPlot.Plot.Axes.SetLimits(minX, maxX, -1000, 65535);
                    }
                    SpecPlot.Refresh();
                });
            });

            // 【状态恢复策略】：防止页面来回切换时图表变白。主动向 ViewModel 索取历史缓存。
            if (e.NewValue is GD_ControlCenter_WPF.ViewModels.ControlPanelViewModel vm)
            {
                bool needRefresh = false;

                if (vm.IsReferenceFileLoaded && vm.LoadedReferenceData != null)
                {
                    _cachedReferenceData = vm.LoadedReferenceData;
                    needRefresh = true;
                }

                if (vm.LatestSpectralData != null)
                {
                    _currentData = vm.LatestSpectralData;
                    needRefresh = true;
                }

                if (needRefresh)
                {
                    Dispatcher.Invoke(() =>
                    {
                        RenderPlot(_currentData);
                        if (_currentData == null)
                        {
                            SpecPlot.Plot.Axes.AutoScale();
                            SpecPlot.Refresh();
                        }
                    });
                }
            }
        }

        #endregion

        #region 4. 图表核心渲染引擎 (Render Engine)

        /// <summary>
        /// 带有 33ms 节流阀的数据更新请求入口。
        /// </summary>
        private void OnPlotUpdateRequested(SpectralData data)
        {
            // 防洪堤：如果两次渲染时间差小于 33ms (~30FPS)，直接抛弃这一帧，保护 UI 线程不卡死
            if ((DateTime.Now - _lastRenderTime).TotalMilliseconds < 33) return;

            _lastRenderTime = DateTime.Now;

            // 异步抛回主线程画图
            Dispatcher.BeginInvoke(new Action(() => RenderPlot(data)));
        }

        /// <summary>
        /// 物理绘制方法。支持双图层（参考与实时）叠加。
        /// </summary>
        private void RenderPlot(SpectralData? liveData)
        {
            // 1. 毁灭性重建：清空所有旧图层，防止多线程引发的内存泄漏和寻峰文本重叠
            SpecPlot.Plot.Clear();

            // 2. 图层 底层：绘制参考文件（红色虚线）
            if (_cachedReferenceData != null)
            {
                var refPlot = SpecPlot.Plot.Add.Scatter(_cachedReferenceData.Wavelengths, _cachedReferenceData.Intensities);
                refPlot.MarkerSize = 0;
                refPlot.LineWidth = 1.0f;
                refPlot.Color = ScottPlot.Color.FromHex("#F44336");
            }

            // 3. 图层 顶层：绘制实时硬件光谱（紫色实线）
            if (liveData != null && liveData.Wavelengths != null && liveData.Wavelengths.Length > 0)
            {
                _currentData = liveData;
                var livePlot = SpecPlot.Plot.Add.Scatter(liveData.Wavelengths, liveData.Intensities);
                livePlot.MarkerSize = 0;
                livePlot.LineWidth = 1.5f;
                livePlot.Color = ScottPlot.Color.FromHex(_primaryColorHex);

                // 根据当前数据决定是否要限制坐标轴缩放
                HandleScalingLogic(liveData);

                // 4. 寻峰图层：在波形之上画出所有被追踪的垂直标记线
                var currentLimits = SpecPlot.Plot.Axes.GetLimits();
                DrawTrackedPeaks(currentLimits.Top);
            }

            // 5. 将刚才安排好的剧本推向屏幕
            SpecPlot.Refresh();
        }

        /// <summary>
        /// 处理坐标轴的缩放与边界限制。
        /// </summary>
        private void HandleScalingLogic(SpectralData data)
        {
            if (_isFirstFrame)
            {
                // 第一帧到达时，提取硬件的波长首尾，设定死“最大观察视野”边界
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

                // 执行一次完美自适应
                SpecPlot.Plot.Axes.AutoScale();
                _isFirstFrame = false;
            }
            else if (_requestAutoScale)
            {
                // 响应用户的强行自适应请求
                SpecPlot.Plot.Axes.AutoScale();
                _requestAutoScale = false;
            }
        }

        /// <summary>
        /// 绘制寻峰标记线。
        /// </summary>
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

        /// <summary>
        /// 辅助方法：将像素距离换算为真实的波长物理容差。
        /// </summary>
        private double CalculateToleranceByPixels(int pixelCount, double minTolerance)
        {
            double xAxisSpan = SpecPlot.Plot.Axes.Bottom.Range.Span;
            double plotWidth = SpecPlot.ActualWidth;
            double nmPerPixel = plotWidth > 0 ? xAxisSpan / plotWidth : 0.1;
            return Math.Max(nmPerPixel * pixelCount, minTolerance);
        }

        #endregion
    }
}