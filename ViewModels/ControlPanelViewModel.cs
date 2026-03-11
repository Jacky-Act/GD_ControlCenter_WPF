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

        private bool _isToggling = false; // 防连点锁

        public ControlPanelViewModel(GeneralDeviceService generalDeviceService, BatteryService batteryService,
            HighVoltageService highVoltageService, JsonConfigService jsonConfigService)
        {
            _generalDeviceService = generalDeviceService;
            _batterySvc = batteryService;
            _highVoltageService = highVoltageService;
            _jsonConfigService = jsonConfigService;

            // --- 读取本地配置 ---
            var appConfig = _jsonConfigService.Load();
            var specConfig = new SpectrometerConfig();

            // 如果配置值大于 0，则使用配置值；否则保留 SpectrometerConfig 中的默认值
            if (appConfig.LastIntegrationTime > 0) specConfig.IntegrationTimeMs = appConfig.LastIntegrationTime;
            if (appConfig.LastAveragingCount > 0) specConfig.AveragingCount = appConfig.LastAveragingCount;

            // 初始化光谱仪包装器，并注入保存配置的逻辑
            SpecVM = new SpectrometerViewModel(specConfig, null!)
            {
                SaveConfigAction = () =>
                {
                    var config = _jsonConfigService.Load();
                    config.LastIntegrationTime = SpecVM.IntegrationTimeMs;
                    config.LastAveragingCount = SpecVM.AveragingCount;
                    _jsonConfigService.Save(config);
                }
            };

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
        //[RelayCommand]
        //private async Task ToggleSpectrometer()
        //{
        //    // 获取当前管理的服务实例
        //    var service = SpectrometerManager.Instance.Devices.FirstOrDefault();

        //    // 如果没有设备，先尝试发现一次
        //    if (service == null)
        //    {
        //        int count = await SpectrometerManager.Instance.DiscoverAndInitDevicesAsync();
        //        if (count == 0) { StatusInfo = "未检测到光谱仪"; return; }
        //        service = SpectrometerManager.Instance.Devices.First();
        //    }

        //    if (!SpecVM.IsCurrentlyMeasuring)
        //    {
        //        StatusInfo = "正在初始化硬件...";
        //        if (await service.InitializeAsync())
        //        {
        //            // --- 新增：将 UI 配置同步给硬件 Service ---
        //            service.Config.IntegrationTimeMs = SpecVM.IntegrationTimeMs;
        //            service.Config.AveragingCount = SpecVM.AveragingCount;

        //            SpecVM.SetService(service);
        //            SpecVM.Model.SerialNumber = service.Config.SerialNumber;
        //            SpecVM.Model.IsConnected = true; // 确保标记为已连接
        //            SpecVM.UpdateFromModel();

        //            service.StartContinuousMeasurement();

        //            SpecVM.IsCurrentlyMeasuring = true;
        //            StatusInfo = $"采集进行中: {SpecVM.SerialNumber}";
        //        }
        //    }
        //    else
        //    {
        //        service.StopMeasurement();
        //        SpecVM.IsCurrentlyMeasuring = false;
        //        StatusInfo = "采集已停止";
        //    }
        //}

        ///// <summary>
        ///// 光谱仪启停切换逻辑（支持多机联机识别）
        ///// </summary>
        //[RelayCommand]
        //private async Task ToggleSpectrometer()
        //{
        //    // 1. 获取设备，若无则尝试扫描
        //    if (SpectrometerManager.Instance.Devices.Count == 0)
        //    {
        //        StatusInfo = "正在扫描光谱仪...";
        //        int count = await SpectrometerManager.Instance.DiscoverAndInitDevicesAsync();
        //        if (count == 0)
        //        {
        //            StatusInfo = "未检测到光谱仪";
        //            return;
        //        }
        //    }

        //    var devices = SpectrometerManager.Instance.Devices;
        //    int deviceCount = devices.Count;

        //    if (!SpecVM.IsCurrentlyMeasuring)
        //    {
        //        StatusInfo = "正在初始化硬件...";
        //        bool allSuccess = true;

        //        // 2. 遍历初始化所有设备，并强行同步 UI 全局配置
        //        foreach (var service in devices)
        //        {
        //            if (await service.InitializeAsync())
        //            {
        //                service.Config.IntegrationTimeMs = SpecVM.IntegrationTimeMs;
        //                service.Config.AveragingCount = SpecVM.AveragingCount;
        //            }
        //            else
        //            {
        //                allSuccess = false;
        //                break;
        //            }
        //        }

        //        if (allSuccess)
        //        {
        //            // 3. 注入首个设备以兼容原有状态绑定，动态修改序列号显示
        //            SpecVM.SetService(devices.First());
        //            SpecVM.Model.SerialNumber = deviceCount == 1 ? devices.First().Config.SerialNumber : "联机模式";
        //            SpecVM.Model.IsConnected = true;
        //            SpecVM.UpdateFromModel();

        //            // 4. 调用 Manager 进行全局启动
        //            SpectrometerManager.Instance.StartAll();

        //            SpecVM.IsCurrentlyMeasuring = true;
        //            StatusInfo = deviceCount == 1
        //                ? $"采集进行中: {devices.First().Config.SerialNumber}"
        //                : $"多机联机采集运行中 (共 {deviceCount} 台)";
        //        }
        //        else
        //        {
        //            StatusInfo = "部分设备初始化失败";
        //        }
        //    }
        //    else
        //    {
        //        // 5. 调用 Manager 进行全局停止
        //        SpectrometerManager.Instance.StopAll();
        //        SpecVM.IsCurrentlyMeasuring = false;
        //        StatusInfo = "采集已停止";
        //    }
        //}

        /// <summary>
        /// 光谱仪启停切换逻辑（支持多机联机识别）
        /// </summary>
        [RelayCommand]
        private async Task ToggleSpectrometer()
        {
            if (_isToggling) return; // 防止快速连点死锁
            _isToggling = true;

            try
            {
                if (SpectrometerManager.Instance.Devices.Count == 0)
                {
                    StatusInfo = "正在扫描光谱仪...";
                    int count = await SpectrometerManager.Instance.DiscoverAndInitDevicesAsync();
                    if (count == 0) { StatusInfo = "未检测到光谱仪"; return; }
                }

                var devices = SpectrometerManager.Instance.Devices;
                int deviceCount = devices.Count;

                if (!SpecVM.IsCurrentlyMeasuring)
                {
                    StatusInfo = "正在初始化硬件...";

                    bool allSuccess = await Task.Run(async () =>
                    {
                        bool success = true;
                        foreach (var service in devices)
                        {
                            // 修复：判断是否已经连接，已连接则直接返回 true，未连接才执行 InitializeAsync
                            bool isInitialized = service.Config.IsConnected || await service.InitializeAsync();

                            if (isInitialized)
                            {
                                await service.UpdateConfigurationAsync(SpecVM.IntegrationTimeMs, SpecVM.AveragingCount);
                            }
                            else
                            {
                                success = false;
                                break;
                            }
                        }
                        if (success) SpectrometerManager.Instance.StartAll();
                        return success;
                    });

                    if (allSuccess)
                    {
                        SpecVM.SetService(devices.First());
                        SpecVM.Model.SerialNumber = deviceCount == 1 ? devices.First().Config.SerialNumber : "联机模式";
                        SpecVM.Model.IsConnected = true;
                        SpecVM.UpdateFromModel();
                        SpecVM.IsCurrentlyMeasuring = true;
                        StatusInfo = deviceCount == 1 ? $"采集进行中: {devices.First().Config.SerialNumber}" : $"多机联机采集运行中 (共 {deviceCount} 台)";
                    }
                    else
                    {
                        StatusInfo = "部分设备初始化失败";
                    }
                }
                else
                {
                    StatusInfo = "正在安全停止硬件...";
                    // 后台执行停止，等待底层 C++ 线程安全退出
                    await Task.Run(() => SpectrometerManager.Instance.StopAll());
                    SpecVM.IsCurrentlyMeasuring = false;
                    StatusInfo = "采集已停止";
                }
            }
            finally
            {
                _isToggling = false; // 释放锁
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
        /// 打开采样配置弹窗
        /// </summary>
        [RelayCommand]
        private void SamplingConfig()
        {
            // 1. 实例化窗口对象
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SamplingSettingWindow();

            // 2. 获取当前配置
            var config = _jsonConfigService.Load();

            // 3. 实例化弹窗的 ViewModel，并传入关闭窗口的回调动作
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.SamplingSettingViewModel(
                _jsonConfigService,
                config,
                () => window.Close());

            // 4. 绑定 DataContext
            window.DataContext = vm;

            // 5. 指定 Owner，使窗口居中于主程序显示（对应 XAML 中的 WindowStartupLocation="CenterOwner"）
            window.Owner = System.Windows.Application.Current.MainWindow;

            // 6. 以模态对话框形式打开
            window.ShowDialog();
        }

        /// <summary>
        /// 打开积分时间配置弹窗
        /// </summary>
        [RelayCommand]
        private void OpenIntegrationTimeConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SpecParamSettingWindow();
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.SpecParamSettingViewModel(
                windowTitle: "积分时间设置",
                headerTitle: "参数配置 - 积分时间 (ms)",
                currentValue: SpecVM.IntegrationTimeMs,
                min: SpecVM.Model.MinIntegrationTime,
                max: 600000,
                step: 5.0,
                applyAction: (val) => SpecVM.ChangeIntegrationTimeCommand.Execute(val),
                closeAction: () => window.Close()
            );
            window.DataContext = vm;
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 打开平均次数配置弹窗
        /// </summary>
        [RelayCommand]
        private void OpenAveragingCountConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SpecParamSettingWindow();
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.SpecParamSettingViewModel(
                windowTitle: "平均次数设置",
                headerTitle: "参数配置 - 平均次数",
                currentValue: SpecVM.AveragingCount,
                min: 1,
                max: 10000,
                step: 1.0,
                applyAction: (val) => SpecVM.ChangeAveragingCountCommand.Execute(val),
                closeAction: () => window.Close()
            );
            window.DataContext = vm;
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();
        }

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