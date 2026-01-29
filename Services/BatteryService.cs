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
        private bool _isWired;        // 是否接入外部电线供电 (由充电MOS开启判定) 

        [ObservableProperty]
        private bool _isCharging;     // 是否正在执行充电动作 (电流 > 0) 

        [ObservableProperty]
        private bool _isBatteryAssisting; // 电池辅助放电状态：接入电线但外部电源功率不足，电池在倒贴出电 

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
            if (frame.Length < 34) return;

            try
            {
                // 解析电流 (Index 6,7)：单位 10mA，高字节在前，正充负放 
                short currRaw = (short)((frame[6] << 8) | frame[7]);

                // 解析剩余容量 (Index 23) 
                Percentage = frame[23];

                // 解析 FET 控制状态 (Index 24)：bit0: 充电 MOS 状态；bit1: 放电 MOS 状态 
                byte mosStatus = frame[24];
                bool chargeMosOpen = (mosStatus & 0x01) != 0;

                // --- 业务逻辑判定 ---

                // 判定外部供电：只要充电 MOS 开启，即视为有外部电源输入 
                IsWired = chargeMosOpen;

                // 判定充电动作：电流矢量为正 
                IsCharging = currRaw > 0;

                // 判定电池辅助放电：已接电线但电流仍为负，说明外部功率无法覆盖负载，电池正在出电补充 
                IsBatteryAssisting = IsWired && currRaw < 0;

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
