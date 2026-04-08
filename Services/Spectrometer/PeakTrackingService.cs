using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Collections.ObjectModel;
using static GD_ControlCenter_WPF.Models.Spectrometer.SpectralData;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    public class PeakTrackingService
    {
        public ObservableCollection<TrackedPeak> TrackedPeaks { get; } = new();

        public PeakTrackingService()
        {
            // 核心：监听全局光谱数据，每一帧回来都自动更新所有峰的实时位置
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                UpdatePeaks(m.Value);
            });
        }

        public void AddPeak(double wavelength)
        {
            if (!TrackedPeaks.Any(p => Math.Abs(p.BaseWavelength - wavelength) < 0.5))
                TrackedPeaks.Add(new TrackedPeak(wavelength));
        }

        public void ClearAll() => TrackedPeaks.Clear();

        private void UpdatePeaks(SpectralData data)
        {
            if (data == null || TrackedPeaks.Count == 0) return;
            foreach (var peak in TrackedPeaks)
            {
                // 调用算法：在标记点附近 2nm 范围内寻找真正的最高点
                double realX = SpectrometerLogic.GetActualPeakWavelength(data, peak.BaseWavelength, peak.ToleranceWindow);
                peak.CurrentWavelength = realX;
                peak.CurrentIntensity = SpectrometerLogic.GetIntensityAtWavelength(data, realX);
            }
        }
    }
}
