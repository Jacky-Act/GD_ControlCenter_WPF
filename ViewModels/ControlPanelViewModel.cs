using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System.Collections.ObjectModel;
using System.Timers;

namespace GD_ControlCenter_WPF.ViewModels
{
    /// <summary>
    /// 控制面板主逻辑
    /// 职责：统筹各硬件服务、管理设备卡片集合、控制光谱仪按钮
    /// </summary>
    public partial class ControlPanelViewModel : ObservableObject
    {
        // --- 核心服务与子 VM ---    
        [ObservableProperty] private SpectrometerViewModel _specVM; // 光谱仪
        [ObservableProperty] private ObservableCollection<DeviceBaseViewModel> _devices = new();    // 设备卡片集合
        [ObservableProperty] private BatteryService _batterySvc;    // 电池

        // --- 布局控制 ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullScreenButtonText))]
        private double _dashboardHeight;    // 上方卡片区高度
        private double _originalHeight; // 初始计算高度备份
        [ObservableProperty] private double _paramHeaderHeight = 35;    // 参数栏高度
        [ObservableProperty] private string _fullScreenButtonText = "大图显示";
        [ObservableProperty] private string _statusInfo = "系统就绪";

        // --- 硬件控制服务 ---
        private readonly GeneralDeviceService _generalDeviceService;
        private readonly HighVoltageService _highVoltageService;
        private readonly JsonConfigService _jsonConfigService;




        // 3. 定义转向阀状态属性
        [ObservableProperty]
        private bool _isSteeringValveActive;

        public ControlPanelViewModel(GeneralDeviceService generalDeviceService, BatteryService batteryService, 
            HighVoltageService highVoltageService, JsonConfigService jsonConfigService)
        {
            _generalDeviceService = generalDeviceService;
            _batterySvc = batteryService;
            _highVoltageService = highVoltageService;
            _jsonConfigService = jsonConfigService;

            // 初始化光谱仪包装器
            SpecVM = new SpectrometerViewModel(new SpectrometerConfig(), null!);

            // 启动电池监控
            _batterySvc.Start();
            InitializeDevices();
        }

        private void InitializeDevices()
        {
            // 初始化高压电源并加入集合
            Devices.Add(new HighVoltageViewModel(_highVoltageService, _jsonConfigService));

            // 初始化蠕动泵并加入集合
            Devices.Add(new PeristalticPumpViewModel(_generalDeviceService, _jsonConfigService));

            // 初始化注射泵并加入集合
            Devices.Add(new SyringePumpViewModel(_generalDeviceService, _jsonConfigService));

            // 后续增加 3D 平台或其他设备只需在此 Add 即可
        }

        // --- 交互命令 ---

        /// <summary>
        /// 光谱仪启停切换逻辑
        /// </summary>
        [RelayCommand]
        private async Task ToggleSpectrometer()
        {
            // 获取当前管理的服务实例
            var service = SpectrometerManager.Instance.Devices.FirstOrDefault();

            // 如果没有设备，先尝试发现一次
            if (service == null)
            {
                int count = await SpectrometerManager.Instance.DiscoverAndInitDevicesAsync();
                if (count == 0) { StatusInfo = "未检测到光谱仪"; return; }
                service = SpectrometerManager.Instance.Devices.First();
            }

            if (!SpecVM.IsCurrentlyMeasuring)
            {
                StatusInfo = "正在初始化硬件...";
                if (await service.InitializeAsync())
                {
                    SpecVM.SetService(service); // 在此处将真实的 service 注入包装器 ---
                    // 同步身份并启动
                    SpecVM.Model.SerialNumber = service.Config.SerialNumber;
                    SpecVM.UpdateFromModel();
                    service.StartContinuousMeasurement();

                    SpecVM.IsCurrentlyMeasuring = true;
                    StatusInfo = $"采集进行中: {SpecVM.SerialNumber}";
                }
            }
            else
            {
                service.StopMeasurement();
                SpecVM.IsCurrentlyMeasuring = false;
                StatusInfo = "采集已停止";
            }
        }

        /// <summary>
        /// 光谱图大图/小图切换逻辑
        /// </summary>
        [RelayCommand]
        private void ToggleFullScreen()
        {
            if (DashboardHeight > 0)
            {
                DashboardHeight = 0;
                FullScreenButtonText = "还原显示";
            }
            else
            {
                DashboardHeight = _originalHeight;
                FullScreenButtonText = "大图显示";
            }
        }

        /// <summary>
        /// 转向阀门的切换
        /// </summary>
        partial void OnIsSteeringValveActiveChanged(bool value) => _generalDeviceService.ControlSteeringValve(value);

        /// <summary>
        /// 点火按钮逻辑
        /// </summary>
        [RelayCommand]
        private void Fire() => _generalDeviceService.Fire();

        /// <summary>
        /// 外部（如 MainWindow）初始化高度时调用
        /// </summary>
        public void SetInitialHeight(double height)
        {
            DashboardHeight = height;
            _originalHeight = height;           
            ParamHeaderHeight = Math.Max(35, height * 0.15);    // 动态设置参数栏高度
        }      
    }
}