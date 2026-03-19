using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    /// <summary>
    /// 特征峰追踪服务 (全局单例)
    /// 职责：维护当前用户标记的所有特征峰，并在每次接收到新光谱数据时，利用底层数学算法更新它们的真实坐标。
    /// </summary>
    public class PeakTrackingService
    {
        /// <summary>
        /// 当前正在追踪的所有特征峰集合。
        /// 使用 ObservableCollection 以便未来此时序图或数据列表直接进行 UI 绑定。
        /// </summary>
        public ObservableCollection<TrackedPeak> TrackedPeaks { get; } = new ObservableCollection<TrackedPeak>();

        // 【新增】：构造函数中订阅全局光谱数据，实现在后台独立更新寻峰坐标
        public PeakTrackingService()
        {
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                UpdatePeaksWithNewData(m.Value);
            });
        }

        /// <summary>
        /// 添加一个新的追踪峰
        /// </summary>
        /// <param name="targetWavelength">鼠标点击处的横坐标</param>
        public void AddPeak(double targetWavelength)
        {
            // 防呆：如果已经存在非常接近的峰（比如 0.5nm 内），就不重复添加了
            if (TrackedPeaks.Any(p => Math.Abs(p.BaseWavelength - targetWavelength) < 0.5))
            {
                return;
            }

            TrackedPeaks.Add(new TrackedPeak(targetWavelength));
        }

        /// <summary>
        /// 移除距离指定波长最近的一个峰
        /// </summary>
        /// <param name="clickWavelength">鼠标点击处的横坐标</param>
        /// <param name="clickTolerance">判定点击生效的像素/波长范围（默认±5nm）</param>
        public void RemovePeakNear(double clickWavelength, double clickTolerance = 5.0)
        {
            if (TrackedPeaks.Count == 0) return;

            // 找到距离鼠标点击位置最近的那个峰
            var closestPeak = TrackedPeaks
                .Select(p => new { Peak = p, Distance = Math.Abs(p.CurrentWavelength - clickWavelength) })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            // 如果这个最近的峰在容差范围内，就把它删掉
            if (closestPeak != null && closestPeak.Distance <= clickTolerance)
            {
                TrackedPeaks.Remove(closestPeak.Peak);
            }
        }

        /// <summary>
        /// 清除所有追踪峰
        /// </summary>
        public void ClearAll()
        {
            TrackedPeaks.Clear();
        }

        /// <summary>
        /// 核心刷新逻辑：根据最新的一帧光谱数据，更新所有峰的实时坐标
        /// </summary>
        /// <param name="currentData">当前帧的光谱数据</param>
        public void UpdatePeaksWithNewData(SpectralData currentData)
        {
            if (currentData == null || currentData.Wavelengths == null || TrackedPeaks.Count == 0)
                return;

            foreach (var peak in TrackedPeaks)
            {
                // 1. 调用已有的逻辑：在 BaseWavelength 附近寻找真正的波长峰值
                double realX = SpectrometerLogic.GetActualPeakWavelength(currentData, peak.BaseWavelength, peak.ToleranceWindow);

                // 2. 调用已有的逻辑：获取该波长对应的真实光强
                double realY = SpectrometerLogic.GetIntensityAtWavelength(currentData, realX);

                // 3. 更新模型坐标
                peak.CurrentWavelength = realX;
                peak.CurrentIntensity = realY;
            }
        }
    }
}