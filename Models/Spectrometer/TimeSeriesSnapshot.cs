using System;

namespace GD_ControlCenter_WPF.Models.Spectrometer
{
    /// <summary>
    /// 时序图单帧数据快照 (用于内存极速缓存)
    /// </summary>
    public class TimeSeriesSnapshot
    {
        /// <summary>
        /// 采样时间戳 (精确到毫秒)
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 该时刻对应各个追踪波长的强度值集合
        /// </summary>
        public double[] Intensities { get; set; } = Array.Empty<double>();
    }
}