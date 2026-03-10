using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 标准串口服务：负责硬件交互、协议解析预处理及消息分发
    /// </summary>
    public partial class SerialPortService : ISerialPortService, IDisposable
    {
        private SerialPort? _serialPort;
        private readonly JsonConfigService _configService;

        public event Action<bool>? ConnectionStatusChanged;

        public bool IsOpen { get; private set; }
        public string? CurrentPortName { get; private set; }

        public SerialPortService(JsonConfigService configService)
        {
            _configService = configService;
        }

        public void AutoConnect()
        {
            var config = _configService.Load();
            var lastPort = config.LastSerialPort;
            var ports = GetAvailablePorts();

            if (!string.IsNullOrEmpty(lastPort) && ports.Contains(lastPort) && !IsOpen)
            {
                Open(lastPort, 9600);
            }
        }

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

                _serialPort.DataReceived += OnSerialDataReceived;
                _serialPort.Open();

                // 核心：更新内部状态并对外触发事件
                UpdateConnectionStatus(true, portName);

                var config = _configService.Load();
                config.LastSerialPort = portName;
                _configService.Save(config);

                return true;
            }
            catch (Exception)
            {
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

            // 核心：更新内部状态并对外触发事件
            UpdateConnectionStatus(false, null);
        }

        /// <summary>
        /// 统一处理状态变更和事件触发
        /// </summary>
        private void UpdateConnectionStatus(bool isOpen, string? portName)
        {
            if (IsOpen != isOpen || CurrentPortName != portName)
            {
                IsOpen = isOpen;
                CurrentPortName = portName;
                ConnectionStatusChanged?.Invoke(IsOpen);
            }
        }

        /// <summary>
        /// 发送 Hex 字节数组
        /// </summary>
        public void Send(byte[] data)
        {
            if (IsOpen) _serialPort?.Write(data, 0, data.Length);
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
                WeakReferenceMessenger.Default.Send(new HexDataMessage(buffer));
            }
            catch (Exception) { }
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
