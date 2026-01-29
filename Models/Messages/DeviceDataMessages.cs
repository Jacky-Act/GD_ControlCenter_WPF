using CommunityToolkit.Mvvm.Messaging.Messages;

namespace GD_ControlCenter_WPF.Models.Messages
{
    /// <summary>
    /// 电池数据消息（34字节完整帧）
    /// </summary>
    public class BatteryFrameMessage : ValueChangedMessage<byte[]>
    {
        public BatteryFrameMessage(byte[] value) : base(value) { }
    }

    /// <summary>
    /// 三维平台位置消息（8字节完整帧）
    /// </summary>
    public class Platform3DMessage : ValueChangedMessage<byte[]>
    {
        public Platform3DMessage(byte[] value) : base(value) { }
    }

    /// <summary>
    /// 控制板标准响应消息（13字节完整帧）
    /// </summary>
    public class ControlResponseMessage : ValueChangedMessage<byte[]>
    {
        public ControlResponseMessage(byte[] value) : base(value) { }
    }
}
