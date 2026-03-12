namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 指令优先级枚举
    /// </summary>
    public enum CommandPriority
    {
        High = 0, // 硬件操控指令（默认）
        Low = 1   // 状态轮询查询指令
    }

    /// <summary>
    /// 串口服务接口：定义串口操作的标准行为
    /// </summary>
    public interface ISerialPortService
    {
        // 使用标准事件向外广播状态变更
        event Action<bool>? ConnectionStatusChanged;

        // 属性：获取当前串口是否打开
        bool IsOpen { get; }

        // 当前串口号
        string? CurrentPortName { get; }

        // 获取可用端口名列表
        string[] GetAvailablePorts();

        // 打开串口
        bool Open(string portName, int baudRate);

        // 关闭串口
        void Close();

        // 发送十六进制数据（带优先级）
        void Send(byte[] data, CommandPriority priority = CommandPriority.High);

        void AutoConnect();
    }
}
