using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 标准串口服务：负责硬件交互、协议解析预处理及消息分发
    /// </summary>
    public partial class SerialPortService : ObservableObject, ISerialPortService, IDisposable
    {
        private SerialPort? _serialPort;

        // 使用 Toolkit 自动生成属性：当 IsOpen 改变时，所有订阅者都会收到通知
        [ObservableProperty]
        private bool _isOpen;

        /// <summary>
        /// 获取当前系统可用串口列表
        /// </summary>
        public string[] GetAvailablePorts() => SerialPort.GetPortNames();

        /// <summary>
        /// 开启串口
        /// </summary>
        public bool Open(string portName, int baudRate)
        {
            try
            {
                // 如果当前已开启，先关闭
                if (IsOpen) Close();

                _serialPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                // 订阅底层原始数据到达事件
                _serialPort.DataReceived += OnSerialDataReceived;

                _serialPort.Open();
                IsOpen = _serialPort.IsOpen; // 自动触发 PropertyChanged
                return true;
            }
            catch (Exception)
            {
                // 可以在这里记录日志或通过 Messenger 发送错误消息
                return false;
            }
        }

        /// <summary>
        /// 关闭串口并释放资源
        /// </summary>
        public void Close()
        {
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= OnSerialDataReceived;
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }
            IsOpen = false;
        }

        /// <summary>
        /// 发送 Hex 字节数组
        /// </summary>
        public void Send(byte[] data)
        {
            if (IsOpen)
            {
                _serialPort?.Write(data, 0, data.Length);
            }
        }

        /// <summary>
        /// 内部数据接收处理器 (运行在次线程)
        /// </summary>
        private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                int bytesToRead = _serialPort.BytesToRead;
                byte[] buffer = new byte[bytesToRead];
                _serialPort.Read(buffer, 0, bytesToRead);

                // --- 核心解耦点 ---
                // 使用 Toolkit 的强类型信使发送 Hex 数据包
                // 任何订阅了 HexDataMessage 的组件都能收到，无需引用本类实例
                WeakReferenceMessenger.Default.Send(new HexDataMessage(buffer));
            }
            catch (Exception)
            {
                // 异常处理逻辑
            }
        }

        /// <summary>
        /// 销毁对象，确保资源释放
        /// </summary>
        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }
}
