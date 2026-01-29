using CommunityToolkit.Mvvm.Messaging.Messages;

namespace GD_ControlCenter_WPF.Models.Messages
{
    /// <summary>
    /// 定义一个用于传输十六进制字节数组的消息包
    /// </summary>
    public class HexDataMessage : ValueChangedMessage<byte[]>
    {
        public HexDataMessage(byte[] value) : base(value)
        {
        }
    }
}
