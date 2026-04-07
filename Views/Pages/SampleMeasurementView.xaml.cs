using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.ViewModels;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Windows.Controls;
using System.Windows;
using System;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class SampleMeasurementView : UserControl
    {
        // 构造函数：修复了 CS1520 (必须带括号)
        public SampleMeasurementView()
        {
            InitializeComponent();
            SetupPlots();

            // 注册光谱数据监听
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
                double wl = SpecPlot.Plot.Axes.Bottom.Range.Center;
                if (this.DataContext is SampleMeasurementViewModel vm)
                {
                    Dispatcher.Invoke(() => vm.PickedElements.Add($"峰@{wl:F2}"));
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

                // --- 渲染上图：全谱图 ---
                SpecPlot.Plot.Clear();
                var fullLine = SpecPlot.Plot.Add.Scatter(data.Wavelengths, data.Intensities);
                fullLine.MarkerSize = 0;
                fullLine.Color = ScottPlot.Colors.MediumPurple;
                SpecPlot.Plot.Axes.AutoScale();
                SpecPlot.Refresh();

                // --- 渲染下图：局部图 (CS0103 修复) ---
                TrendPlot.Plot.Clear();
                var elementLine = TrendPlot.Plot.Add.Scatter(data.Wavelengths, data.Intensities);
                elementLine.MarkerSize = 0;
                elementLine.Color = ScottPlot.Colors.DeepSkyBlue;

                if (targetWl > 0)
                {
                    TrendPlot.Plot.Axes.SetLimits(targetWl - 2.5, targetWl + 2.5, -100, data.Intensities.Max() + 500);
                }
                TrendPlot.Refresh();

                // --- 数据采集 (如果是采集状态) ---
                if (vm != null && targetWl > 0)
                {
                    double currentIntensity = SpectrometerLogic.GetIntensityAtWavelength(data, targetWl);
                    vm.AddDataToBuffer(currentIntensity);
                }
            });
        }
    }
}