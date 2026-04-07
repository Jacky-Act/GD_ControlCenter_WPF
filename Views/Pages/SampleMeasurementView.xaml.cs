using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using System.Windows.Controls;
using GD_ControlCenter_WPF.ViewModels;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Linq;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class SampleMeasurementView : UserControl
    {
        public SampleMeasurementView()
        {
            InitializeComponent();
            SetupPlots();

            // 订阅光谱数据
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                RenderPlots(m.Value);
            });
        }

        private void SetupPlots()
        {
            SpecPlot.Menu?.Clear();
            SpecPlot.Menu?.Add("寻峰", (p) =>
            {
                double mouseWl = SpecPlot.Plot.Axes.Bottom.Range.Center;
                if (this.DataContext is SampleMeasurementViewModel vm)
                {
                    Dispatcher.Invoke(() => vm.PickedElements.Add($"峰@{mouseWl:F2}"));
                }
            });
            SpecPlot.Menu?.Add("去除所有峰", (p) =>
            {
                if (this.DataContext is SampleMeasurementViewModel vm)
                {
                    Dispatcher.Invoke(() => vm.PickedElements.Clear());
                }
            });
        }

        private void RenderPlots(SpectralData data)
        {
            if (data.Wavelengths == null || data.Wavelengths.Length == 0) return;

            Dispatcher.Invoke(() =>
            {
                var vm = this.DataContext as SampleMeasurementViewModel;
                double targetWl = vm?.GetTargetWavelength() ?? 0;

                // --- 1. 上图：全谱图逻辑 ---
                SpecPlot.Plot.Clear();
                var fullLine = SpecPlot.Plot.Add.Scatter(data.Wavelengths, data.Intensities);
                fullLine.MarkerSize = 0;
                fullLine.Color = ScottPlot.Colors.MediumPurple;
                SpecPlot.Plot.Axes.AutoScale();
                SpecPlot.Refresh();

                // --- 2. 下图：元素局部谱图逻辑 ---
                TrendPlot.Plot.Clear();
                var elementLine = TrendPlot.Plot.Add.Scatter(data.Wavelengths, data.Intensities);
                elementLine.MarkerSize = 0;
                elementLine.Color = ScottPlot.Colors.DeepSkyBlue;

                if (targetWl > 0)
                {
                    // 局部放大：波长左右各 2nm 范围
                    TrendPlot.Plot.Axes.SetLimits(targetWl - 2, targetWl + 2, -100, data.Intensities.Max() + 500);
                }
                TrendPlot.Refresh();

                // --- 3. 数据采集逻辑 ---
                if (vm != null && targetWl > 0)
                {
                    double currentIntensity = SpectrometerLogic.GetIntensityAtWavelength(data, targetWl);
                    vm.AddDataToBuffer(currentIntensity);
                }
            });
        }
    }
}
