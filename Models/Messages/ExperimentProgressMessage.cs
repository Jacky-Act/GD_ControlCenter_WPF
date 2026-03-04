using CommunityToolkit.Mvvm.Messaging.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;

/*
 * 文件名: ExperimentProgressMessage.cs
 * 描述: 定义实验进度相关的复杂数据传输消息。
 * 主要用于实验过程中，同步当前的采样点数据及整体实验状态。
 * 当前只写了空间分布实验的内容
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Models.Messages
{
    /// <summary>
    /// 空间分布实验进度消息。
    /// 负责在 Logic 逻辑层与 UI 视图模型之间传输 <see cref="SpatialExperimentData"/> 实体。
    /// 该消息触发时，UI 层将同步更新实验轨迹及采样状态列表。
    /// </summary>
    public class SpatialExperimentMessage : ValueChangedMessage<SpatialExperimentData>
    {
        public SpatialExperimentMessage(SpatialExperimentData value) : base(value)
        {
        }
    }
}
