using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Protocols;
using GD_ControlCenter_WPF.Services.Commands;
using System.Timers;

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 高压电源服务：负责定时下发 0x66 查询指令、参数设置以及解析回传的高压电源电压与电流数值。
    /// </summary>
    public partial class HighVoltageService : ObservableObject, IDisposable
    {
        private readonly ISerialPortService _serialPortService;
        private readonly System.Timers.Timer _pollingTimer;

        // 看门狗：10秒未收到有效回传则判定为通讯离线
        private DateTime _lastReceivedTime = DateTime.MinValue;
        private const int MaxOfflineSeconds = 10;

        #region 向外暴露的业务属性

        [ObservableProperty]
        private int _voltage;      // 实时电压值 (单位：V)，范围 0-1500V 

        [ObservableProperty]
        private int _current;      // 实时电流值 (单位：mA)，范围 0-100mA 

        [ObservableProperty]
        private bool _isOnline;    // 通讯是否在线

        #endregion

        public HighVoltageService(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;

            // 初始化轮询定时器：设置为 1 秒触发一次查询
            _pollingTimer = new System.Timers.Timer(1000);
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;

            // 注册消息订阅：接收来自 ProtocolService 的 13 字节标准响应帧
            WeakReferenceMessenger.Default.Register<ControlResponseMessage>(this, (r, m) =>
            {
                ParseResponse(m.Value);
            });
        }

        /// <summary>
        /// 启动高压监控任务
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
        /// 停止高压监控
        /// </summary>
        public void Stop()
        {
            _pollingTimer.Stop();
            IsOnline = false;
        }

        /// <summary>
        /// 设置高压电源参数
        /// </summary>
        /// <param name="targetVoltage">目标电压 (0-1500V) </param>
        /// <param name="targetCurrent">目标电流 (0-100mA) </param>
        public void SetHighVoltage(int targetVoltage, int targetCurrent)
        {
            // 调用工厂生成的 0x22 设置指令包 
            var cmd = ControlCommandFactory.CreateHighVoltage(targetVoltage, targetCurrent);
            _serialPortService.Send(cmd);
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
        /// 通过串口发送功能码为 0x66 的查询指令
        /// </summary>
        private void SendQuery()
        {
            // 指令序列：68 aa 01 66 0d 00 00 00 00 00 xx xx 16 
            var cmd = ControlCommandFactory.CreateHighVoltageQuery();
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 核心解析与判断方法
        /// </summary>
        /// <param name="frame">13 字节完整标准帧</param>
        private void ParseResponse(byte[] frame)
        {
            // 判定功能码是否为 0x66 (ReturnPowerInfo) 
            if (frame.Length == 13 && (FunctionCode)frame[3] == FunctionCode.ReturnPowerInfo)
            {
                try
                {
                    // 解析电压：Index 6(高字节), Index 7(低字节)，范围 0-1500V 
                    Voltage = (frame[6] << 8) | frame[7];

                    // 解析电流：Index 8(高字节), Index 9(低字节)，范围 0-100mA 
                    Current = (frame[8] << 8) | frame[9];

                    IsOnline = true;
                    _lastReceivedTime = DateTime.Now;
                }
                catch (Exception)
                {
                    // 异常处理逻辑，防止坏包导致程序崩溃
                }
            }
        }

        public void Dispose()
        {
            _pollingTimer?.Dispose();
            WeakReferenceMessenger.Default.Unregister<ControlResponseMessage>(this);
        }
    }
}
