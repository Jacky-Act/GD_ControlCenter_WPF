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
        /// 多设备光谱拼接算法 (峰值保护版 - 终极形态)。
        /// 特性1：使用全局静态内存池，过程零分配 (Zero-Allocation)。
        /// 特性2：采用“最大值保持 (Peak Hold)”处理重合波长，完美杜绝“平均算法”导致的尖峰强度被削弱现象。
        /// </summary>
        /// <param name="dataCollection">等待拼接的多台光谱仪原始数据集合。</param>
        /// <param name="mergeTolerance">重合容差(nm)。建议设为 0.05，仅合并距离极近的像素点。</param>
        public static SpectralData? PerformStitching(IEnumerable<SpectralData> dataCollection, double mergeTolerance = 0.05)
        {
            if (dataCollection == null) return null;

            lock (_stitchLock)
            {
                int totalLength = 0;
                int count = 0;

                // 预扫描：计算总长度
                foreach (var data in dataCollection)
                {
                    if (data.Wavelengths != null) totalLength += data.Wavelengths.Length;
                    count++;
                }

                if (count < 2 || totalLength == 0) return null;

                // 动态扩容
                if (totalLength > _bufferWavelengths.Length)
                {
                    _maxBufferSize = totalLength * 2;
                    _bufferWavelengths = new double[_maxBufferSize];
                    _bufferIntensities = new double[_maxBufferSize];
                }

                // 步骤 1：数据打平拷贝
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

                // 步骤 2：按照波长原位升序排列
                Array.Sort(_bufferWavelengths, _bufferIntensities, 0, totalLength);

                // 步骤 3：重合区间去重与峰值保护 (Peak Hold)
                int validCount = 0; // 慢指针：指向合并后的有效数据尾部

                // 快指针 i：向后探测
                for (int i = 1; i < totalLength; i++)
                {
                    // 判断当前探测到的波长，是否与慢指针的波长极为接近 (发生重合)
                    if (Math.Abs(_bufferWavelengths[i] - _bufferWavelengths[validCount]) <= mergeTolerance)
                    {
                        // 【核心修改】：坚决不求平均！只保留强度最大的那一个！
                        if (_bufferIntensities[i] > _bufferIntensities[validCount])
                        {
                            // 如果新探测到的点更亮，用它覆盖掉慢指针处的数据
                            _bufferWavelengths[validCount] = _bufferWavelengths[i];
                            _bufferIntensities[validCount] = _bufferIntensities[i];
                        }
                        // 如果新探测的点较弱，则直接丢弃（快指针 i 继续往下走，慢指针 validCount 不动）
                    }
                    else
                    {
                        // 不重合：慢指针向前推进一步，存入独立的新波长点
                        validCount++;
                        _bufferWavelengths[validCount] = _bufferWavelengths[i];
                        _bufferIntensities[validCount] = _bufferIntensities[i];
                    }
                }

                // 有效数据的真实总长度
                int finalLength = validCount + 1;

                // 步骤 4：拷贝出结果
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
        /// 局部动态寻峰 (防跳峰优化版)：在指定的理论波长容差窗口内，锁定实际的最高峰值波长。
        /// 核心算法：基于“局部最高点 (Local Maxima) 就近匹配”，完美解决距离极近的双峰/多峰互相干扰跳跃的问题。
        /// </summary>
        /// <param name="data">当前帧光谱数据。</param>
        /// <param name="targetWavelength">用户期望寻找的理论中心波长（鼠标点击位置）。</param>
        /// <param name="tolerance">允许的左右搜索范围（± nm）。</param>
        public static double GetActualPeakWavelength(SpectralData data, double targetWavelength, double tolerance)
        {
            if (data == null || data.Wavelengths == null || data.Wavelengths.Length == 0) return targetWavelength;

            // 记录容差窗口内的绝对最高点（作为纯斜坡环境下的兜底保障）
            double maxIntensityInWindow = -1;
            int maxIndexInWindow = -1;

            // 记录容差窗口内所有“局部山头 (Local Peaks)”的索引名单
            var localPeakIndices = new System.Collections.Generic.List<int>();

            for (int i = 0; i < data.Wavelengths.Length; i++)
            {
                double currentW = data.Wavelengths[i];

                // 仅在容差窗口内进行搜索
                if (Math.Abs(currentW - targetWavelength) <= tolerance)
                {
                    double currentI = data.Intensities[i];

                    // 1. 随时更新窗口内的绝对最高点 (兜底用)
                    if (currentI > maxIntensityInWindow)
                    {
                        maxIntensityInWindow = currentI;
                        maxIndexInWindow = i;
                    }

                    // 2. 识别“局部山头”：当前像素必须严格大于其左右相邻的像素
                    // 处理数组越界的边界安全
                    double leftI = (i > 0) ? data.Intensities[i - 1] : double.MinValue;
                    double rightI = (i < data.Wavelengths.Length - 1) ? data.Intensities[i + 1] : double.MinValue;

                    if (currentI > leftI && currentI > rightI)
                    {
                        localPeakIndices.Add(i);
                    }
                }
            }

            // 场景 A：如果窗口内没有任何“山头”（说明这是一个平缓的长坡或截断区），只能返回窗口内的最高点
            if (localPeakIndices.Count == 0)
            {
                return maxIndexInWindow != -1 ? data.Wavelengths[maxIndexInWindow] : targetWavelength;
            }

            // 场景 B：窗口内发现了 1 个或多个“山头”（双峰重叠区）
            // 【核心防跳峰逻辑】：忽略山头的高度，选出一个距离用户 TargetWavelength 最近的山头！
            int bestPeakIndex = localPeakIndices
                .OrderBy(idx => Math.Abs(data.Wavelengths[idx] - targetWavelength))
                .First();

            return data.Wavelengths[bestPeakIndex];
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