using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using System.Collections.Concurrent;
using System.IO.Ports;

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 标准串口服务：负责硬件交互、协议解析预处理及消息分发
    /// 加入了生产者-消费者队列，保障并发写入时的线程安全与硬件处理间隔
    /// </summary>
    public partial class SerialPortService : ISerialPortService, IDisposable
    {
        private SerialPort? _serialPort;
        private readonly JsonConfigService _configService;

        // --- 并发队列控制相关字段 ---
        private readonly ConcurrentQueue<byte[]> _highPriorityQueue = new();
        private readonly ConcurrentQueue<byte[]> _lowPriorityQueue = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cts = new();

        public event Action<bool>? ConnectionStatusChanged;

        public bool IsOpen { get; private set; }
        public string? CurrentPortName { get; private set; }

        public SerialPortService(JsonConfigService configService)
        {
            _configService = configService;

            // 实例化时即启动后台发送队列处理器
            Task.Run(() => ProcessSendQueueAsync(_cts.Token));
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

            // 清空未发送的队列指令，避免下次打开时堆积发送
            _highPriorityQueue.Clear();
            _lowPriorityQueue.Clear();

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
        /// 发送 Hex 字节数组（推入队列，不阻塞当前线程）
        /// </summary>
        public void Send(byte[] data, CommandPriority priority = CommandPriority.High)
        {
            if (priority == CommandPriority.High)
            {
                _highPriorityQueue.Enqueue(data);
            }
            else
            {
                _lowPriorityQueue.Enqueue(data);
            }

            // 释放一个信号，唤醒后台消费线程
            _signal.Release();
        }

        /// <summary>
        /// 消费者：单线程持续提取并发送指令
        /// </summary>
        private async Task ProcessSendQueueAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 等待队列中有数据进入
                    await _signal.WaitAsync(token);

                    byte[]? commandToSend = null;

                    // 严格的优先级校验：永远先尝试从高优先级队列取数据
                    if (_highPriorityQueue.TryDequeue(out var highCmd))
                    {
                        commandToSend = highCmd;
                    }
                    else if (_lowPriorityQueue.TryDequeue(out var lowCmd))
                    {
                        commandToSend = lowCmd;
                    }

                    // 只有在串口打开的情况下才执行写入
                    if (commandToSend != null && _serialPort != null && _serialPort.IsOpen)
                    {
                        try
                        {
                            _serialPort.Write(commandToSend, 0, commandToSend.Length);

                            // 强制安全间隔：确保下位机有充足时间处理上一帧（这里设为30ms）
                            await Task.Delay(200, token);
                        }
                        catch (Exception)
                        {
                            // 如果写入瞬间串口被拔出等异常，吞掉异常或打印日志，防止整个任务崩溃
                        }
                    }
                    // 如果串口没打开但队列里有数据（比如刚启动还没连接时触发了发送），数据会自动出队并丢弃，防止爆内存
                }
            }
            catch (OperationCanceledException)
            {
                // 任务正常取消退出
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
                WeakReferenceMessenger.Default.Send(new HexDataMessage(buffer));
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 销毁对象，确保资源释放
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            Close();
            _cts.Dispose();
            _signal.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}