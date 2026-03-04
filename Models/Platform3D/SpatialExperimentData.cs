
/*
 * 文件名: SpatialExperimentData.cs
 * 描述: 定义空间分布实验的单个数据采集点实体。
 * 用于记录采样时的物理坐标、时间戳以及所属的实验轮次，是后续生成实验报表和光谱分布图的基础。
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Models.Platform3D
{
    /// <summary>
    /// 空间分布实验数据实体。
    /// 记录了在一个特定的空间坐标点位下，采集所产生的完整业务信息。
    /// </summary>
    public class SpatialExperimentData
    {
        /// <summary>
        /// 当前采集点的序号（第几个位置点）
        /// </summary>
        public int PointIndex { get; set; }

        /// <summary>
        /// 当前点位下的采集轮次（在该位置进行的第几次采样）
        /// </summary>
        public int AcquisitionRound { get; set; }

        /// <summary>
        /// 采样时位移轴的物理位置（脉冲步数）
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// 数据生成的精确时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 关联的轴类型
        /// </summary>
        public AxisType Axis { get; set; }
    }
}
