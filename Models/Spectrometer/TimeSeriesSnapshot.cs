using System;

/*
 * 文件名: TimeSeriesSnapshot.cs
 * 描述: 定义用于时序图显示的极简数据快照结构。
 * 职责：专为高频采样设计，剥离冗余波长信息，仅记录时间戳与特定峰的强度集合。
 */

namespace GD_ControlCenter_WPF.Models.Spectrometer
{
    /// <summary>
    /// 时序图单帧数据快照。
    /// </summary>
    public class TimeSeriesSnapshot
    {
        /// <summary>
        /// 数据采集生成的精确时刻。
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 该时刻对应各个追踪波长点的光强值数组（单位：Counts）。
        /// 数组索引必须与“特征峰追踪列表”的顺序一致。
        /// </summary>
        public double[] Intensities { get; set; } = Array.Empty<double>();

        public TimeSeriesSnapshot() { }

        public TimeSeriesSnapshot(DateTime timestamp, double[] intensities)
        {
            Timestamp = timestamp;
            Intensities = intensities;
        }
    }
}