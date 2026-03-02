using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
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

        #region 向外暴露的业务属性

        [ObservableProperty]
        private int _percentage;      // 剩余容量百分比 (RSOC) 

        [ObservableProperty]
        private bool _isOnline;       // 通讯是否在线

        [ObservableProperty]
        private bool _isCharging;     // 是否正在执行充电动作 (电流 > 0) 
        #endregion

        public BatteryService(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;

            // 初始化轮询定时器：建议间隔 30000ms
            _pollingTimer = new System.Timers.Timer(3000);
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;

            // 注册消息订阅：接收由 ProtocolService 处理好的 34 字节电池帧
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
                _pollingTimer.Start();
                SendQuery(); // 启动时立即查询一次
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
            // 通讯超时检查逻辑
            if (IsOnline && (DateTime.Now - _lastReceivedTime).TotalSeconds > MaxOfflineSeconds)
            {
                IsOnline = false;
            }
            SendQuery();
        }

        /// <summary>
        /// 通过串口发送电池查询指令
        /// </summary>
        private void SendQuery()
        {
            // 指令序列：DD A5 03 00 FF FD 77
            var cmd = ControlCommandFactory.CreateBatteryQuery();
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 核心解析与逻辑判断方法
        /// </summary>
        /// <param name="frame">由 ProtocolService 截取的完整 34 字节数据包 </param>
        private void ParseBatteryData(byte[] frame)
                                                                                                                                                       {
            System.Diagnostics.Debug.WriteLine("收到电池数据帧");
            if (frame.Length < 34) return;

            try
            {
                // 解析电流数据 (Index 6, 7)
                byte byte6 = frame[6];
                byte byte7 = frame[7];

                // 解析剩余容量 (Index 23) 
                Percentage = frame[23];

                // --- 充电判定逻辑 ---
                // 逻辑：
                // 1. 未充电判断：第六字节最高位为 1 (代表负电流/放电) OR (第六字节和第七字节均为 0)
                // 2. 充电判断：第六字节最高位为 0 (代表正电流) AND (第六字节和第七字节至少一个不为 0)
                bool isNegativeOrZero = (byte6 >> 7 == 1) || (byte6 == 0 && byte7 == 0);
                IsCharging = !isNegativeOrZero;

                // 更新通讯状态
                IsOnline = true; 
                _lastReceivedTime = DateTime.Now;
            }
            catch (Exception)
            {
                // 异常处理逻辑，防止坏包导致程序崩溃
            }
        }

        public void Dispose()
        {
            _pollingTimer?.Dispose();
            WeakReferenceMessenger.Default.Unregister<BatteryFrameMessage>(this);
        }
    }
}
