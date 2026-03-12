using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Protocols;
using GD_ControlCenter_WPF.Services.Commands;
using System.Timers;

/*
 * 文件名: HighVoltageService.cs
 * 描述: 高压电源控制与监控服务。
 * 负责高压电源的参数设置（功能码 0x22）与实时数据轮询（功能码 0x66）。
 * 内部集成定时器进行 1s 一次的自动查询，并通过消息中心接收和解析回传的电压、电流数值。
 * 包含简单的看门狗逻辑，用于判断通讯是否离线。
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 高压电源服务类。
    /// 核心逻辑：定时下发查询指令 -> 接收响应消息 -> 解析数据并更新 UI 绑定属性。
    /// </summary>
    public partial class HighVoltageService : ObservableObject, IDisposable
    {
        private readonly ISerialPortService _serialPortService;
        private readonly System.Timers.Timer _pollingTimer;

        // 看门狗：10秒未收到有效回传则判定为通讯离线
        private DateTime _lastReceivedTime = DateTime.MinValue;
        private const int MaxOfflineSeconds = 10;

        public int Voltage { get; private set; }      // 实时电压值 (V)
        public int Current { get; private set; }      // 实时电流值 (mA)
        public bool IsOnline { get; private set; }    // 硬件通讯在线状态

        public HighVoltageService(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;

            // 配置轮询定时器：1000ms 触发一次查询
            _pollingTimer = new System.Timers.Timer(1000);
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;

            // 订阅 ControlResponseMessage：接收由 ProtocolService 分发的 13 字节标准帧
            WeakReferenceMessenger.Default.Register<ControlResponseMessage>(this, (r, m) =>
            {
                ParseResponse(m.Value);
            });
        }

        /// <summary>
        /// 启动高压电源监控任务。
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
        /// 停止监控并清除在线状态。
        /// </summary>
        public void Stop()
        {
            _pollingTimer.Stop();
            IsOnline = false;
        }

        /// <summary>
        /// 设置高压电源参数。
        /// </summary>
        /// <param name="targetVoltage">目标电压 (范围: 0-1500V)</param>
        /// <param name="targetCurrent">目标电流限制 (范围: 0-100mA)</param>
        public void SetHighVoltage(int targetVoltage, int targetCurrent)
        {
            var cmd = ControlCommandFactory.CreateHighVoltage(targetVoltage, targetCurrent);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 定时轮询回调：执行通讯超时判定并触发查询指令发送。
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
        /// 构造并发送功能码为 0x66 的查询请求帧，要求硬件返回一次电压电流值（低优先级）
        /// </summary>
        private void SendQuery()
        {
            var cmd = ControlCommandFactory.CreateHighVoltageQuery();
            // 标记为低优先级查询轮询
            _serialPortService.Send(cmd, CommandPriority.Low);
        }

        /// <summary>
        /// 核心报文解析逻辑。
        /// 验证功能码后，按照协议定义的索引提取电压与电流（高位+低位组合）。
        /// </summary>
        /// <param name="frame">接收到的 13 字节标准完整帧。</param>
        /// 
            
        private void ParseResponse(byte[] frame)
        {
            // 判定功能码是否为 0x66 (ReturnPowerInfo) 
            if (frame.Length == 13 && (FunctionCode)frame[3] == FunctionCode.ReturnPowerInfo)
            {
                try
                {
                    // 回传电压计算 (计算后四舍五入并转为整数)
                    double rawVoltage = frame[7] + (frame[6] & 0x7f) * 256.0;
                    Voltage = (int)Math.Round(rawVoltage / 32768.0 * 2.048 * 5.25 * 150.0);

                    // 回传电流计算 (计算后四舍五入并转为整数)
                    double rawCurrent = frame[9] + (frame[8] & 0x7f) * 256.0;
                    Current = (int)Math.Round(rawCurrent / 32768.0 * 2.048 * 5.25 * 10.0);

                    IsOnline = true;
                    _lastReceivedTime = DateTime.Now;

                    // 触发属性变更通知，唤醒 ViewModel 更新 UI
                    OnPropertyChanged(nameof(Voltage));
                    OnPropertyChanged(nameof(Current));
                    OnPropertyChanged(nameof(IsOnline));
                }
                catch (Exception)
                {
                    // 异常处理逻辑
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
