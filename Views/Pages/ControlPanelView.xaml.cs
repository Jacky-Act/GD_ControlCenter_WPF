using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using ScottPlot;
using System;
using System.Windows;
using System.Windows.Controls;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class ControlPanelView : UserControl
    {
        private bool _isFirstFrame = true;
        private DateTime _lastRenderTime = DateTime.MinValue;

        // 预定义 Material Design 风格的主题色
        private readonly string _primaryColorHex = "#673ab7";
        // 用于缓存全局最大范围
        private AxisLimits? _maxLimits = null;

        public ControlPanelView()
        {
            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;

            // --- 自定义图表右键菜单 ---
            SpecPlot.Menu?.Clear(); // 清除默认菜单

            SpecPlot.Menu?.Add("寻峰", (plotControl) =>
            {
                // TODO: 寻峰处理逻辑
            });

            SpecPlot.Menu?.Add("去除附近一条", (plotControl) =>
            {
                // TODO: 去除附近一条处理逻辑
            });

            SpecPlot.Menu?.Add("去除全部", (plotControl) =>
            {
                // TODO: 去除全部处理逻辑
            });
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);

            // 1. 监听数据更新
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                OnPlotUpdateRequested(m.Value);
            });

            // 2. 监听自动范围
            WeakReferenceMessenger.Default.Register<AutoRangeRequestMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    SpecPlot.Plot.Axes.AutoScale();
                    SpecPlot.Refresh();
                });
            });

            // 3. 监听最大范围
            WeakReferenceMessenger.Default.Register<MaxRangeRequestMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_maxLimits.HasValue)
                    {
                        SpecPlot.Plot.Axes.SetLimits(_maxLimits.Value);
                        SpecPlot.Refresh();
                    }
                });
            });
        }

        private void OnPlotUpdateRequested(SpectralData data)
        {
            // UI 渲染限流：约 30 FPS
            if ((DateTime.Now - _lastRenderTime).TotalMilliseconds < 33)
                return;

            _lastRenderTime = DateTime.Now;

            // 异步触发绘制，直接把数据传过去
            Dispatcher.BeginInvoke(new Action(() => RenderPlot(data)));
        }

        private void RenderPlot(SpectralData data)
        {
            if (data.Wavelengths == null || data.Wavelengths.Length == 0) return;

            SpecPlot.Plot.Clear();

            var line = SpecPlot.Plot.Add.Scatter(data.Wavelengths, data.Intensities);
            line.MarkerSize = 0;
            line.LineWidth = 1.5f;
            line.Color = ScottPlot.Color.FromHex(_primaryColorHex);

            if (_isFirstFrame)
            {
                double minWavelength = data.Wavelengths[0];
                double maxWavelength = data.Wavelengths[^1];

                // 计算并缓存全局最大限制范围
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

            SpecPlot.Refresh();
        }
    }
}