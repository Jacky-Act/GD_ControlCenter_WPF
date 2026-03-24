using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Protocols;
using GD_ControlCenter_WPF.Services.Commands;
using System.Timers;

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 电池业务服务：负责定时轮询电池状态，并结合电流矢量与 MOSFET 状态判定复杂的供电工况 。
    /// </summary>
    public partial class BatteryService : ObservableObject, IDisposable
    {
        private readonly ISerialPortService _serialPortService;
        private readonly System.Timers.Timer _pollingTimer;

        // 看门狗：10秒未收到有效回传则判定为通讯离线
        private DateTime _lastReceivedTime = DateTime.MinValue;
        private const int MaxOfflineSeconds = 10;

        // 新增状态更新事件
        public event EventHandler? StatusUpdated;

        // 替换为原生只读属性
        public int Percentage { get; private set; }
        public bool IsOnline { get; private set; }
        public bool IsCharging { get; private set; }

        public BatteryService(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;

            // 初始化轮询定时器：间隔 30s
            _pollingTimer = new System.Timers.Timer(30000);
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;

            // 注册消息订阅：接收由 ProtocolService 处理好的 13 字节电池帧
            WeakReferenceMessenger.Default.Register<BatteryFrameMessage>(this, (r, m) =>
            {
                ParseBatteryData(m.Value);
            });
        }

        /// <summary>
        /// 启动电池监控任务
        /// </summary>
        public void Start()
        {
            if (!_pollingTimer.Enabled)
            {
                _pollingTimer.Start(); // 启动 30 秒的底层轮询定时器

                SendQuery(); // 第 1 次查询：启动时立即执行

                // 第 2 次查询：利用后台任务延时 1 秒后执行
                Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    // 再次检查定时器状态，防止在这 1 秒内用户触发了 Stop() 关闭了连接
                    if (_pollingTimer.Enabled)
                    {
                        SendQuery();
                    }
                });
            }
        }

        /// <summary>
        /// 停止电池监控
        /// </summary>
        public void Stop()
        {
            _pollingTimer.Stop();
            IsOnline = false;
        }

        /// <summary>
        /// 定时器触发逻辑：检查通讯超时并下发查询指令
        /// </summary>
        private void OnPollingTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (IsOnline && (DateTime.Now - _lastReceivedTime).TotalSeconds > MaxOfflineSeconds)
            {
                IsOnline = false;
                StatusUpdated?.Invoke(this, EventArgs.Empty); // 通知离线
            }
            SendQuery();
        }

        /// <summary>
        /// 通过串口发送电池查询指令（低优先级）
        /// </summary>
        private void SendQuery()
        {
            var cmd = ControlCommandFactory.CreateBatteryQuery();
            // 标记为低优先级，避免阻塞硬件控制指令
            _serialPortService.Send(cmd, CommandPriority.Low);
        }

        /// <summary>
        /// 核心报文解析逻辑。
        /// 验证功能码后，按照协议定义的索引提取电流状态与剩余容量。
        /// </summary>
        /// <param name="frame">接收到的 13 字节标准完整帧。</param>
        private void ParseBatteryData(byte[] frame)
        {
            if (frame.Length == 13 && (FunctionCode)frame[3] == FunctionCode.Battery)
            {
                try
                {
                    byte currentHigh = frame[6];
                    byte currentLow = frame[7];

                    Percentage = frame[8];

                    bool isNegativeOrZero = (currentHigh >> 7 == 1) || (currentHigh == 0 && currentLow == 0);
                    IsCharging = !isNegativeOrZero;

                    IsOnline = true;
                    _lastReceivedTime = DateTime.Now;

                    // 解析完成后触发事件，通知 ViewModel 更新
                    StatusUpdated?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            _pollingTimer?.Dispose();
            WeakReferenceMessenger.Default.Unregister<BatteryFrameMessage>(this);
        }
    }
}
