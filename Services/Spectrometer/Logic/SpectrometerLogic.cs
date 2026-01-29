using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using System.Collections.Concurrent;

namespace GD_ControlCenter_WPF.Services.Spectrometer.Logic
{
    /// <summary>
    /// 光谱处理逻辑类
    /// 职责：负责峰值检测、采样时间计算、以及多设备光谱拼接算法。
    /// </summary>
    public class SpectrometerLogic
    {
        private readonly ISpectrometerService _spectrometerService;

        /// <summary>
        /// 暂存多台设备最近的光谱数据，Key 为序列号。
        /// 用于在多机协作场景中进行全谱拼接。
        /// </summary>
        private readonly ConcurrentDictionary<string, SpectralData> _latestDataCache = new();

        public SpectrometerLogic(ISpectrometerService spectrometerService)
        {
            _spectrometerService = spectrometerService ?? throw new ArgumentNullException(nameof(spectrometerService));

            // 订阅单台光谱仪发出的原始数据消息
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                OnSpectralDataReceived(m.Value);
            });
        }

        /// <summary>
        /// 核心处理逻辑：每当收到新光谱数据时触发
        /// </summary>
        private void OnSpectralDataReceived(SpectralData data)
        {
            // 自动执行饱和检查
            if (_spectrometerService.Config.IsSaturationDetectionEnabled)
            {
                CheckSaturation(data);
            }
            
            _latestDataCache[data.SourceDeviceSerial] = data;   // 更新缓存

            // 3. 如果需要实时拼接，可以在此处调用
            // PerformStitching();
        }

        /// <summary>
        /// 寻找光谱中的最大强度及其对应的波长（GD 机器寻优核心算法）
        /// </summary>
        /// <param name="data">光谱数据实体。</param>
        /// <returns>返回 (波长, 最大强度) 的元组。</returns>
        public (double wavelength, double intensity) FindPeak(SpectralData data)
        {
            if (data == null || data.Intensities.Length == 0) return (0, 0);

            double maxIntensity = data.Intensities.Max();
            int index = Array.IndexOf(data.Intensities, maxIntensity);
            double wavelength = data.Wavelengths[index];

            return (wavelength, maxIntensity);
        }

        /// <summary>
        /// 多设备光谱拼接算法：去重、排序并生成全谱
        /// </summary>
        public void PerformStitching()
        {
            if (_latestDataCache.Count < 2) return; // 只有一台设备时不执行拼接

            // 将所有缓存的数据合并到一个列表中
            var allPoints = _latestDataCache.Values
                .SelectMany(d => d.Wavelengths.Zip(d.Intensities, (w, i) => new { W = w, I = i }))
                .OrderBy(p => p.W) // 按波长升序排列
                .ToList();

            // 简单的去重逻辑：如果波长极度接近，取平均值或最大值
            var stitchedWavelengths = allPoints.Select(p => p.W).ToArray();
            var stitchedIntensities = allPoints.Select(p => p.I).ToArray();
            var combinedData = new SpectralData(stitchedWavelengths, stitchedIntensities, "Combined_System");

            // 发送拼接完成的消息
            WeakReferenceMessenger.Default.Send(new CombinedSpectralMessage(combinedData));
        }

        /// <summary>
        /// 1. 动态寻峰：在容差窗口内锁定真实的峰值波长（应对波长漂移）
        /// </summary>
        public double GetActualPeakWavelength(SpectralData data, double targetWavelength, double tolerance)
        {
            if (data == null || data.Wavelengths.Length == 0) return targetWavelength;

            double maxIntensity = -1;
            double actualWavelength = targetWavelength; // 默认返回理论值

            for (int i = 0; i < data.Wavelengths.Length; i++)
            {
                double currentW = data.Wavelengths[i];

                // 检查是否在搜索窗口内
                if (Math.Abs(currentW - targetWavelength) <= tolerance)
                {
                    if (data.Intensities[i] > maxIntensity)
                    {
                        maxIntensity = data.Intensities[i];
                        actualWavelength = currentW; // 记录最强点对应的真实波长
                    }
                }

                // 性能优化：波长升序，超出窗口后退出
                if (currentW > targetWavelength + tolerance) break;
            }
            return actualWavelength;
        }

        /// <summary>
        /// 2. 定点取值：获取光谱中与指定波长最接近的像素强度
        /// </summary>
        public double GetIntensityAtWavelength(SpectralData data, double wavelength)
        {
            if (data == null || data.Wavelengths.Length == 0) return 0;

            int bestIndex = 0;
            double minDiff = double.MaxValue;

            for (int i = 0; i < data.Wavelengths.Length; i++)
            {
                double diff = Math.Abs(data.Wavelengths[i] - wavelength);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestIndex = i;
                }
                else if (data.Wavelengths[i] > wavelength)
                {
                    // 既然是升序，差异开始变大时说明已经越过最接近点
                    break;
                }
            }
            return data.Intensities[bestIndex];
        }

        /// <summary>
        /// 校验光谱是否达到硬件饱和
        /// </summary>
        private void CheckSaturation(SpectralData data)
        {
            if (data.Intensities.Any(i => i > 65000))   // 16位 ADC
            {
                // 发送状态变更消息提醒 UI
                WeakReferenceMessenger.Default.Send(new SpectrometerStatusMessage(_spectrometerService.Config));
            }
        }
    }
}
