using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer;

namespace GD_ControlCenter_WPF.ViewModels
{
    /// <summary>
    /// 光谱仪 UI 包装器
    /// 职责：同步 Model 数据、接收硬件消息、管理按钮状态与事件分发
    /// </summary>
    public partial class SpectrometerViewModel : ObservableObject
    {
        private readonly SpectrometerConfig _model;
        private ISpectrometerService _service; // 引入服务接口

        // --- UI 交互属性 ---

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MeasurementButtonText))]
        private bool _isCurrentlyMeasuring;

        /// <summary>
        /// 按钮显示文本（由 IsCurrentlyMeasuring 自动驱动）
        /// </summary>
        public string MeasurementButtonText => IsCurrentlyMeasuring ? "停止光谱" : "启动光谱";

        // --- 硬件参数属性 ---

        [ObservableProperty] private string _serialNumber = string.Empty; 
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private float _integrationTimeMs;
        [ObservableProperty] private uint _averagingCount;

        // --- 事件与消息 ---

        /// <summary>
        /// 绘图更新事件：由 View 层订阅
        /// </summary>
        public event Action<SpectralData>? RequestPlotUpdate;

        // 用于保存配置的委托
        public Action? SaveConfigAction { get; set; }

        public SpectrometerViewModel(SpectrometerConfig model, ISpectrometerService service)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _service = service; // 这里不再 throw
            UpdateFromModel();  // 构造时立即从 Model 加载数据

            //注册弱引用消息订阅
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                // 校验消息是否属于当前设备 
                if (m.Value.SourceDeviceSerial == this.SerialNumber)
                {                   
                    RequestPlotUpdate?.Invoke(m.Value); // 触发绘图更新事件
                }
            });
        }

        // --- 业务逻辑方法 ---

        /// <summary>
        /// 从 Model 同步基础数据至 ViewModel
        /// </summary>
        public void UpdateFromModel()
        {
            SerialNumber = _model.SerialNumber;
            IsConnected = _model.IsConnected;
            IntegrationTimeMs = _model.IntegrationTimeMs;
            AveragingCount = _model.AveragingCount;
        }

        // 3. 提供一个方法，在连接硬件后注入 Service
        public void SetService(ISpectrometerService service)
        {
            _service = service;
        }

        // --- 属性更改钩子 (同步回 Model) ---

        partial void OnIntegrationTimeMsChanged(float value) => _model.IntegrationTimeMs = value;   // 当 UI 修改积分时间时，同步更新 Model
        partial void OnAveragingCountChanged(uint value) => _model.AveragingCount = value;  // 当 UI 修改平均次数时，同步更新 Model

        public SpectrometerConfig Model => _model;

        // --- 参数修改逻辑 ---

        [RelayCommand]
        private async Task ChangeIntegrationTimeAsync(string timeStr)
        {
            if (float.TryParse(timeStr, out float newTime))
            {
                IntegrationTimeMs = newTime;
                _model.IntegrationTimeMs = newTime;
                SaveConfigAction?.Invoke();

                var devices = SpectrometerManager.Instance.Devices;
                if (devices.Count > 0)
                {
                    // 在后台执行完整的硬件重启流程，防止 UI 卡死
                    await Task.Run(async () =>
                    {
                        bool wasMeasuring = IsCurrentlyMeasuring;

                        // 1. 如果正在测量，必须先让硬件停下来
                        if (wasMeasuring) SpectrometerManager.Instance.StopAll();

                        // 2. 更新底层硬件配置
                        var updateTasks = devices.Select(device => device.UpdateConfigurationAsync(newTime, AveragingCount));
                        await Task.WhenAll(updateTasks);

                        // 3. 恢复测量
                        if (wasMeasuring) SpectrometerManager.Instance.StartAll();
                    });
                }
            }
        }

        [RelayCommand]
        private async Task ChangeAveragingCountAsync(string countStr)
        {
            if (uint.TryParse(countStr, out uint newCount))
            {
                AveragingCount = newCount;
                _model.AveragingCount = newCount;
                SaveConfigAction?.Invoke();

                var devices = SpectrometerManager.Instance.Devices;
                if (devices.Count > 0)
                {
                    await Task.Run(async () =>
                    {
                        bool wasMeasuring = IsCurrentlyMeasuring;

                        // 1. 先停
                        if (wasMeasuring) SpectrometerManager.Instance.StopAll();

                        // 2. 更新配置
                        var updateTasks = devices.Select(device => device.UpdateConfigurationAsync(IntegrationTimeMs, newCount));
                        await Task.WhenAll(updateTasks);

                        // 3. 恢复
                        if (wasMeasuring) SpectrometerManager.Instance.StartAll();
                    });
                }
            }
        }
    }
}