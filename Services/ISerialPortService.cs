namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 串口服务接口：定义串口操作的标准行为
    /// </summary>
    public interface ISerialPortService
    {
        // 属性：获取当前串口是否打开
        bool IsOpen { get; }

        // 获取可用端口名列表
        string[] GetAvailablePorts();

        // 打开串口
        bool Open(string portName, int baudRate);

        // 关闭串口
        void Close();

        // 发送十六进制数据
        void Send(byte[] data);
    }
}
