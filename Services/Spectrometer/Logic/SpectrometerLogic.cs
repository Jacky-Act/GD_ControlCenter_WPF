using GD_ControlCenter_WPF.Models.Spectrometer;

namespace GD_ControlCenter_WPF.Services.Spectrometer.Logic
{
    /// <summary>
    /// 光谱处理逻辑工具类 (纯静态)
    /// 职责：提供光谱数据的纯数学计算、多设备光谱拼接算法以及饱和度校验。
    /// 本类不保存任何状态，不包含硬件依赖，不直接发送任何全局消息。
    /// </summary>
    public static class SpectrometerLogic
    {
        // =========================================================
        // 全局静态缓存区（避免高频采集时产生海量 GC 垃圾）
        // 假设单台 4096 像素，默认分配 16384 支持 4 台同时拼接
        // =========================================================
        private static int _maxBufferSize = 16384;
        private static double[] _bufferWavelengths = new double[_maxBufferSize];
        private static double[] _bufferIntensities = new double[_maxBufferSize];
        private static int[] _bufferCounts = new int[_maxBufferSize]; // 用于记录重合点数量以计算平均值
        private static readonly object _stitchLock = new object();    // 确保高频并发调用的线程安全

        /// <summary>
        /// 多设备光谱拼接算法 (极致性能版)
        /// 特性1：使用全局静态内存池，过程零分配。
        /// 特性2：对重叠波长区域的数据自动取平均值。
        /// </summary>
        /// <param name="dataCollection">包含多台光谱仪原始数据的集合。</param>
        /// <param name="mergeTolerance">重合容差(nm)，两点波长差异小于此值将被视为重合点并取平均强度。默认0.2nm</param>
        public static SpectralData? PerformStitching(IEnumerable<SpectralData> dataCollection, double mergeTolerance = 0.2)
        {
            if (dataCollection == null) return null;

            // 加锁防止多线程数据错乱
            lock (_stitchLock)
            {
                int totalLength = 0;
                int count = 0;

                foreach (var data in dataCollection)
                {
                    if (data.Wavelengths != null)
                        totalLength += data.Wavelengths.Length;
                    count++;
                }

                if (count < 2 || totalLength == 0) return null;

                // 动态扩容机制（以防将来接入更多设备超出默认缓存池）
                if (totalLength > _bufferWavelengths.Length)
                {
                    _maxBufferSize = totalLength * 2;
                    _bufferWavelengths = new double[_maxBufferSize];
                    _bufferIntensities = new double[_maxBufferSize];
                    _bufferCounts = new int[_maxBufferSize];
                }

                // 1. 数据打平：直接内存拷贝到全局缓存 (极速)
                int currentOffset = 0;
                foreach (var data in dataCollection)
                {
                    if (data.Wavelengths != null && data.Intensities != null)
                    {
                        int len = Math.Min(data.Wavelengths.Length, data.Intensities.Length);
                        Array.Copy(data.Wavelengths, 0, _bufferWavelengths, currentOffset, len);
                        Array.Copy(data.Intensities, 0, _bufferIntensities, currentOffset, len);
                        currentOffset += len;
                    }
                }

                // 2. 原位排序：按波长升序，且强度数组同步交换 (零内存分配)
                Array.Sort(_bufferWavelengths, _bufferIntensities, 0, totalLength);

                // 3. 重合区间求均值 (双指针原地合并算法)
                int validCount = 0; // 合并后的有效索引
                _bufferCounts[0] = 1;

                for (int i = 1; i < totalLength; i++)
                {
                    // 判断当前波长是否与上一个波长 "重合" (差距在容差范围内)
                    if (Math.Abs(_bufferWavelengths[i] - _bufferWavelengths[validCount]) <= mergeTolerance)
                    {
                        // 累加强度和重合次数
                        _bufferIntensities[validCount] += _bufferIntensities[i];
                        _bufferCounts[validCount]++;
                    }
                    else
                    {
                        // 结算上一个重合点的最终平均值
                        if (_bufferCounts[validCount] > 1)
                        {
                            _bufferIntensities[validCount] /= _bufferCounts[validCount];
                        }

                        // 移动到下一个新点
                        validCount++;
                        _bufferWavelengths[validCount] = _bufferWavelengths[i];
                        _bufferIntensities[validCount] = _bufferIntensities[i];
                        _bufferCounts[validCount] = 1; // 重置计数
                    }
                }

                // 结算循环结束后的最后一个点的平均值
                if (_bufferCounts[validCount] > 1)
                {
                    _bufferIntensities[validCount] /= _bufferCounts[validCount];
                }

                int finalLength = validCount + 1;

                // 4. 拷贝出结果
                // 这里是整个算法唯一一次 new 内存（必须生成干净的结果给 UI 显示）
                double[] finalW = new double[finalLength];
                double[] finalI = new double[finalLength];
                Array.Copy(_bufferWavelengths, 0, finalW, 0, finalLength);
                Array.Copy(_bufferIntensities, 0, finalI, 0, finalLength);

                return new SpectralData(finalW, finalI, "Combined_System");
            }
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
            }
            return data.Intensities[bestIndex];
        }
    }
}