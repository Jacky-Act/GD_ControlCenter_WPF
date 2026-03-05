using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.ViewModels;
using ScottPlot;
using System.Windows;
using System.Windows.Controls;

namespace GD_ControlCenter_WPF.Views.Pages
{
    /// <summary>
    /// 光谱控制面板视图后台
    /// 职责：处理 ScottPlot 控件的实时渲染逻辑
    /// </summary>
    public partial class ControlPanelView : UserControl
    {
        // 缓存 Scatter 对象，避免频繁创建导致性能下降
        private ScottPlot.Plottables.Scatter? _spectralLine;
        private bool _isFirstFrame = true; // 添加类成员变量标记首帧

        public ControlPanelView()
        {
            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is ControlPanelViewModel vm && vm.SpecVM != null)
            {
                // 弱订阅确保事件在页面切换时能正确解耦
                vm.SpecVM.RequestPlotUpdate += (data) =>
                {
                    // 硬件回调通常在后台线程，必须回到 UI 线程刷新
                    Dispatcher.Invoke(() => UpdatePlot(data));
                };
            }
        }

        /// <summary>
        /// 执行光谱数据渲染
        /// </summary>
        private void UpdatePlot(SpectralData data)
        {
            if (data.Wavelengths == null || data.Wavelengths.Length == 0) return;

            // 方案：完全清除法（适合 GD 机器初期调试，确保坐标轴始终自适应）
            SpecPlot.Plot.Clear();

            _spectralLine = SpecPlot.Plot.Add.Scatter(data.Wavelengths, data.Intensities);
            _spectralLine.MarkerSize = 0; // 关闭散点，仅显示线条提升性能
            _spectralLine.LineWidth = 1.5f;
            _spectralLine.Color = ScottPlot.Color.FromHex("#673ab7"); // 使用 Primary 主题色

            // --- 核心逻辑：仅在第一帧固定坐标轴 ---
            if (_isFirstFrame)
            {
                double minWavelength = data.Wavelengths[0];
                double maxWavelength = data.Wavelengths[^1]; // 获取最后一个波长元素

                // A. 初始定位：将 X 轴直接对齐到当前硬件的物理波长范围
                SpecPlot.Plot.Axes.SetLimitsX(minWavelength, maxWavelength);

                // B. 设置轴限制规则：最小只能缩到该波长范围，无法看到范围外的空白
                // 参数说明：(目标轴, 限制边界) - 注意 ScottPlot 5 不带 axis: 命名参数
                // 1. 定义物理边界范围 (X轴波长范围, Y轴强度范围)
                var limits = new AxisLimits(minWavelength, maxWavelength, 0, 65535);

                // 2. 修正构造函数：同时传入 Bottom(X轴) 和 Left(Y轴)
                var xLimitRule = new ScottPlot.AxisRules.MaximumBoundary(
                    SpecPlot.Plot.Axes.Bottom,
                    SpecPlot.Plot.Axes.Left,
                    limits
                );

                SpecPlot.Plot.Axes.Rules.Clear();
                SpecPlot.Plot.Axes.Rules.Add(xLimitRule);

                _isFirstFrame = false;
            }

            SpecPlot.Refresh();
        }
    }
}
