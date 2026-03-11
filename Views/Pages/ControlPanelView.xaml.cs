using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using ScottPlot;
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

        public ControlPanelView()
        {
            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                OnPlotUpdateRequested(m.Value);
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

                var limits = new AxisLimits(minWavelength, maxWavelength, 0, 65535);
                var limitRule = new ScottPlot.AxisRules.MaximumBoundary(
                    SpecPlot.Plot.Axes.Bottom,
                    SpecPlot.Plot.Axes.Left,
                    limits
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