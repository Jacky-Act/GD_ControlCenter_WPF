using GD_ControlCenter_WPF.Models.Spectrometer;

/*
 * 文件名: SpectrometerLogic.cs
 * 模块: 数据算法层 (Data Algorithm Layer)
 * 描述: 提供光谱数据的纯数学计算、多设备光谱拼接算法以及饱和度校验。
 * 架构规范:
 * 1. 【纯函数设计】：本类必须保持为纯静态 (static)，绝对不允许保存任何业务状态。
 * 2. 【无依赖性】：严禁在此类中注入服务、操作 UI 或发送全局 Messenger 消息。它只做算术题。
 * 3. 【极速性能】：拼接算法采用“静态全局内存池”设计，修改时严禁在 for 循环中 new 任何局部对象！
 */

namespace GD_ControlCenter_WPF.Services.Spectrometer.Logic
{
    public static class SpectrometerLogic
    {
        #region 1. 全局静态内存池 (极速拼接基建)

        // =========================================================
        // 【内存池架构说明】
        // 为什么不用普通的 List<T>？
        // 因为在 100Hz 的高频采集下，如果每次拼接都 new 几万个 double 数组，
        // .NET 的垃圾回收器 (GC) 会瞬间爆炸，导致软件出现严重的周期性卡顿。
        // 所以我们在这里预先申请一块巨大的静态内存，所有的拼接都在这块内存上“原地”完成。
        // =========================================================

        private static int _maxBufferSize = 16384; // 默认容量：支持 4 台 4096 像素的设备拼接
        private static double[] _bufferWavelengths = new double[_maxBufferSize];
        private static double[] _bufferIntensities = new double[_maxBufferSize];
        private static int[] _bufferCounts = new int[_maxBufferSize]; // 记录重合点数量，用于计算平均值

        /// <summary>
        /// 拼接算法的专属并发锁。防止多线程同时调用拼接算法导致内存池数据被交叉污染。
        /// </summary>
        private static readonly object _stitchLock = new object();

        #endregion

        #region 2. 多机光谱拼接算法 (Stitching Algorithm)

        /// <summary>
        /// 多设备光谱拼接算法 (极致性能版)。
        /// 特性1：使用全局静态内存池，过程零分配 (Zero-Allocation)。
        /// 特性2：对重叠波长区域的数据自动取平均值 (平滑过渡)。
        /// </summary>
        /// <param name="dataCollection">等待拼接的多台光谱仪原始数据集合。</param>
        /// <param name="mergeTolerance">重合容差(nm)。两点波长差异小于此值将被视为同一点并取平均强度。</param>
        /// <returns>返回拼接后的一条完整光谱数据。若输入无效则返回 null。</returns>
        public static SpectralData? PerformStitching(IEnumerable<SpectralData> dataCollection, double mergeTolerance = 0.2)
        {
            if (dataCollection == null) return null;

            // 必须加锁，因为全局内存池只有一份，绝对不能并发覆写
            lock (_stitchLock)
            {
                int totalLength = 0;
                int count = 0;

                // 预扫描：计算所有数据的总长度
                foreach (var data in dataCollection)
                {
                    if (data.Wavelengths != null)
                        totalLength += data.Wavelengths.Length;
                    count++;
                }

                // 如果只有 1 台设备或没有数据，直接抛弃不拼接
                if (count < 2 || totalLength == 0) return null;

                // 【动态扩容机制】：如果未来接入了 10 台设备超出了默认缓存池，自动翻倍扩容
                if (totalLength > _bufferWavelengths.Length)
                {
                    _maxBufferSize = totalLength * 2;
                    _bufferWavelengths = new double[_maxBufferSize];
                    _bufferIntensities = new double[_maxBufferSize];
                    _bufferCounts = new int[_maxBufferSize];
                }

                // --- 步骤 1：数据打平 (Flatten) ---
                // 将多台设备的波长和强度，直接粗暴地按顺序拷贝到全局缓存池中 (极速内存拷贝)
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

                // --- 步骤 2：原位排序 (In-Place Sort) ---
                // 按照波长升序排列。注意：强度数组会跟着波长数组同步交换位置，所以数据不会乱。
                Array.Sort(_bufferWavelengths, _bufferIntensities, 0, totalLength);

                // --- 步骤 3：重合区间求均值 (双指针原地合并算法) ---
                int validCount = 0; // 慢指针：指向合并后的有效数据尾部
                _bufferCounts[0] = 1;

                // 快指针 i：向后遍历探测
                for (int i = 1; i < totalLength; i++)
                {
                    // 判断当前探测到的波长，是否与慢指针的波长 "重合" (差距在容差范围内)
                    if (Math.Abs(_bufferWavelengths[i] - _bufferWavelengths[validCount]) <= mergeTolerance)
                    {
                        // 发生重叠：累加强度，并记录重叠次数
                        _bufferIntensities[validCount] += _bufferIntensities[i];
                        _bufferCounts[validCount]++;
                    }
                    else
                    {
                        // 结束重叠：先结算上一个重叠点的最终平均值
                        if (_bufferCounts[validCount] > 1)
                        {
                            _bufferIntensities[validCount] /= _bufferCounts[validCount];
                        }

                        // 慢指针向前推进一步，将新探测到的独立波长存入
                        validCount++;
                        _bufferWavelengths[validCount] = _bufferWavelengths[i];
                        _bufferIntensities[validCount] = _bufferIntensities[i];
                        _bufferCounts[validCount] = 1; // 重置新点的计数
                    }
                }

                // 循环结束后，别忘了结算最后一个点的平均值
                if (_bufferCounts[validCount] > 1)
                {
                    _bufferIntensities[validCount] /= _bufferCounts[validCount];
                }

                // 有效数据的真实总长度
                int finalLength = validCount + 1;

                // --- 步骤 4：拷贝出结果 ---
                // 【注意】：这是整个算法唯一一次使用 new 关键字分配内存。
                // 必须生成一份干净的实体对象返回给上层，防止上层持有全局内存池的引用。
                double[] finalW = new double[finalLength];
                double[] finalI = new double[finalLength];
                Array.Copy(_bufferWavelengths, 0, finalW, 0, finalLength);
                Array.Copy(_bufferIntensities, 0, finalI, 0, finalLength);

                return new SpectralData(finalW, finalI, "Combined_System");
            }
        }

        #endregion

        #region 3. 单源数据分析 (单点/寻峰/校验)

        /// <summary>
        /// 校验光谱数据是否达到硬件饱和（过曝）。
        /// 物理依据：基于 16 位 ADC，最大计数值通常为 65535。设定 65000 为安全红线。
        /// </summary>
        public static bool CheckSaturation(SpectralData data)
        {
            if (data == null || data.Intensities == null || data.Intensities.Length == 0) return false;
            return data.Intensities.Any(i => i > 65000);
        }

        /// <summary>
        /// 寻找光谱中的绝对最大强度及其对应的波长（全局最高峰寻优算法）。
        /// </summary>
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
        /// 局部动态寻峰：在指定的理论波长容差窗口内，锁定实际的最高峰值波长。
        /// 物理意义：应对环境温度、震动等引起的微小波长漂移，确保时序图始终追踪真实的峰位。
        /// </summary>
        /// <param name="data">当前帧光谱数据。</param>
        /// <param name="targetWavelength">期望寻找的理论中心波长。</param>
        /// <param name="tolerance">允许的左右搜索范围（± nm）。</param>
        public static double GetActualPeakWavelength(SpectralData data, double targetWavelength, double tolerance)
        {
            if (data == null || data.Wavelengths == null || data.Wavelengths.Length == 0) return targetWavelength;

            double maxIntensity = -1;
            double actualWavelength = targetWavelength;

            for (int i = 0; i < data.Wavelengths.Length; i++)
            {
                double currentW = data.Wavelengths[i];

                // 如果当前波长落入我们的搜索窗口内
                if (Math.Abs(currentW - targetWavelength) <= tolerance)
                {
                    // 记录该窗口内的最大值坐标
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
        /// 定点取值：获取光谱中与指定理论波长在物理上“最接近”的真实像素强度值。
        /// </summary>
        public static double GetIntensityAtWavelength(SpectralData data, double targetWavelength)
        {
            if (data == null || data.Wavelengths == null || data.Wavelengths.Length == 0) return 0;

            int bestIndex = 0;
            double minDiff = double.MaxValue;

            for (int i = 0; i < data.Wavelengths.Length; i++)
            {
                // 找差值最小的那个索引
                double diff = Math.Abs(data.Wavelengths[i] - targetWavelength);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestIndex = i;
                }
            }
            return data.Intensities[bestIndex];
        }

        #endregion
    }
}