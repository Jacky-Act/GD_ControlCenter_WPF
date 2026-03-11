using GD_ControlCenter_WPF.Models.Spectrometer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GD_ControlCenter_WPF.Services.Spectrometer.Logic
{
    /// <summary>
    /// 光谱处理逻辑工具类 (纯静态)
    /// 职责：提供光谱数据的纯数学计算、多设备光谱拼接算法以及饱和度校验。
    /// 本类不保存任何状态，不包含硬件依赖，不直接发送任何全局消息。
    /// </summary>
    public static class SpectrometerLogic
    {
        // --- 公开核心算法方法 ---

        /// <summary>
        /// 多设备光谱拼接算法：对传入的多台设备的数据进行排序、合并，并生成全谱数据。
        /// </summary>
        /// <param name="dataCollection">包含多台光谱仪原始数据的集合。</param>
        /// <returns>返回拼接完成的完整全谱数据实体。若传入数据为空或少于两台，则返回 null。</returns>
        public static SpectralData? PerformStitching(IEnumerable<SpectralData> dataCollection)
        {
            if (dataCollection == null || dataCollection.Count() < 2) return null;

            // 将所有缓存的数据合并到一个列表中，并按波长升序排列
            var allPoints = dataCollection
                .SelectMany(d => d.Wavelengths.Zip(d.Intensities, (w, i) => new { W = w, I = i }))
                .OrderBy(p => p.W)
                .ToList();

            var stitchedWavelengths = allPoints.Select(p => p.W).ToArray();
            var stitchedIntensities = allPoints.Select(p => p.I).ToArray();

            // 生成全谱数据实体，设定统一的系统虚拟标识
            return new SpectralData(stitchedWavelengths, stitchedIntensities, "Combined_System");
        }

        /// <summary>
        /// 校验光谱数据是否达到硬件饱和（过曝）。
        /// </summary>
        /// <param name="data">单次采集的光谱数据实体。</param>
        /// <returns>若存在任何像素强度超过 65000（基于 16 位 ADC），则返回 true；否则返回 false。</returns>
        public static bool CheckSaturation(SpectralData data)
        {
            if (data == null || data.Intensities == null || data.Intensities.Length == 0) return false;

            return data.Intensities.Any(i => i > 65000);
        }

        /// <summary>
        /// 寻找光谱中的绝对最大强度及其对应的波长（GD 机器寻优核心算法）。
        /// </summary>
        /// <param name="data">光谱数据实体。</param>
        /// <returns>返回包含 (波长, 最大强度) 的元组；若数据为空则返回 (0, 0)。</returns>
        public static (double wavelength, double intensity) FindPeak(SpectralData data)
        {
            if (data == null || data.Intensities == null || data.Intensities.Length == 0) return (0, 0);

            double maxIntensity = data.Intensities.Max();
            int index = Array.IndexOf(data.Intensities, maxIntensity);
            double wavelength = data.Wavelengths[index];

            return (wavelength, maxIntensity);
        }

        /// <summary>
        /// 动态寻峰：在指定理论波长的容差窗口内，锁定并返回实际最强点对应的真实波长（用于应对环境温度等引起的波长漂移）。
        /// </summary>
        /// <param name="data">光谱数据实体。</param>
        /// <param name="targetWavelength">期望寻找的理论中心波长。</param>
        /// <param name="tolerance">允许波长偏移的容差范围（± nm）。</param>
        /// <returns>返回容差窗口内实际强度最大处的波长。若窗口内无数据或输入为空，则直接返回原理论波长。</returns>
        public static double GetActualPeakWavelength(SpectralData data, double targetWavelength, double tolerance)
        {
            if (data == null || data.Wavelengths == null || data.Wavelengths.Length == 0) return targetWavelength;

            double maxIntensity = -1;
            double actualWavelength = targetWavelength;

            for (int i = 0; i < data.Wavelengths.Length; i++)
            {
                double currentW = data.Wavelengths[i];

                // 检查是否在搜索窗口内
                if (Math.Abs(currentW - targetWavelength) <= tolerance)
                {
                    if (data.Intensities[i] > maxIntensity)
                    {
                        maxIntensity = data.Intensities[i];
                        actualWavelength = currentW;
                    }
                }

                // 性能优化：数组默认波长为升序，超出窗口后即可提前退出循环
                if (currentW > targetWavelength + tolerance) break;
            }
            return actualWavelength;
        }

        /// <summary>
        /// 定点取值：获取光谱中与指定波长在物理上最接近的有效像素的强度值。
        /// </summary>
        /// <param name="data">光谱数据实体。</param>
        /// <param name="wavelength">需要查询强度的目标波长。</param>
        /// <returns>返回最接近该波长的像素强度；若数据为空则返回 0。</returns>
        public static double GetIntensityAtWavelength(SpectralData data, double wavelength)
        {
            if (data == null || data.Wavelengths == null || data.Wavelengths.Length == 0) return 0;

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
                    // 数组为升序，差异开始变大时说明已经越过了最接近点，可提前退出
                    break;
                }
            }
            return data.Intensities[bestIndex];
        }
    }
}