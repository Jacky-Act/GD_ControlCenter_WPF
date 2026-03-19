/*
 * 文件名: TimeSeriesSnapshot.cs
 * 模块: 数据模型 (Domain Models)
 * 描述: 时序图专用的极简数据快照结构，用于在内存中高频积攒数据并最终一次性落盘。
 */

namespace GD_ControlCenter_WPF.Models.Spectrometer
{
    /// <summary>
    /// 时序图单帧数据快照。
    /// 职责：仅保留核心的“时间戳”与“抽样强度数组”，剥离庞大的波长信息，以实现极低的内存占用。
    /// </summary>
    public class TimeSeriesSnapshot
    {
        /// <summary>
        /// 采样产生时的精确时间戳 (通常保留到毫秒)。
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 该时刻对应各个追踪波长点的强度值集合。
        /// 数组索引与当前追踪的峰位顺序严格对应。
        /// </summary>
        public double[] Intensities { get; set; } = Array.Empty<double>();
    }
}