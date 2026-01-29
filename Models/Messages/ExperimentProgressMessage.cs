using CommunityToolkit.Mvvm.Messaging.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;

namespace GD_ControlCenter_WPF.Models.Messages
{
    /// <summary>
    /// 空间分布实验进度消息：负责在 Logic 层与 ViewModel 之间传输实验数据实体
    /// </summary>
    public class SpatialExperimentMessage : ValueChangedMessage<SpatialExperimentData>
    {
        public SpatialExperimentMessage(SpatialExperimentData value) : base(value)
        {
        }
    }
}
