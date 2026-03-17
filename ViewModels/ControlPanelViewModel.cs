using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using System.Collections.ObjectModel;


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
        [ObservableProperty] private BatteryViewModel _batteryVM; // 电池

        // --- 布局控制 ---
        [ObservableProperty]
        private double _dashboardHeight;    // 上方卡片区高度
        private double _originalHeight; // 初始计算高度备份
        [ObservableProperty] private double _paramHeaderHeight = 35;    // 参数栏高度
        [ObservableProperty] private string _statusInfo = "系统就绪";

        // --- 硬件控制服务 ---
        private readonly GeneralDeviceService _generalDeviceService;
        private readonly JsonConfigService _jsonConfigService;

        // --- 阀门与防连点锁 ---
        [ObservableProperty] private bool _isSteeringValveActive;
        private bool _isToggling = false;

        // --- 自动打火状态追踪 ---
        private readonly HighVoltageViewModel _hvVM;
        private readonly PeristalticPumpViewModel _pumpVM;
        private bool _isPlasmaStable = false;
        private int _reigniteAttemptCount = 0;
        private const int MaxReigniteAttempts = 3;  // 最大尝试次数
        private bool _isReigniting = false;         // 防止打火序列重入锁

        public ControlPanelViewModel(
                    GeneralDeviceService generalDeviceService,
                    JsonConfigService jsonConfigService,
                    BatteryViewModel batteryVM,
                    HighVoltageViewModel highVoltageVM,
                    PeristalticPumpViewModel peristalticPumpVM,
                    SyringePumpViewModel syringePumpVM)
        {
            _generalDeviceService = generalDeviceService;
            _jsonConfigService = jsonConfigService;
            _batteryVM = batteryVM;

            // 1. 保存硬件 VM 实例
            _hvVM = highVoltageVM;
            _pumpVM = peristalticPumpVM;

            // 2. 订阅硬件状态变化，用于自动打火监控
            _hvVM.PropertyChanged += OnHardwareStateChanged;
            _pumpVM.PropertyChanged += OnHardwareStateChanged;

            // --- 读取本地配置 ---
            var appConfig = _jsonConfigService.Load();
            var specConfig = new SpectrometerConfig();

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

            // 直接将注入的 VM 添加到集合中
            Devices.Add(highVoltageVM);
            Devices.Add(peristalticPumpVM);
            Devices.Add(syringePumpVM);

            // 监听光谱仪状态变更消息
            WeakReferenceMessenger.Default.Register<SpectrometerStatusMessage>(this, (r, m) =>
            {
                var incomingConfig = m.Value;

                // 识别出这是来自设置页面的全局通断指令
                if (incomingConfig.SerialNumber == "GLOBAL_SWITCH")
                {
                    bool isEnabled = incomingConfig.IsConnected;

                    if (!isEnabled)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            SpecVM.IsCurrentlyMeasuring = false;
                            SpecVM.Model.IsConnected = false;
                            SpecVM.Model.SerialNumber = "已断开连接";
                            SpecVM.UpdateFromModel();
                            StatusInfo = "光谱仪硬件已被全局禁用";
                            _isToggling = false; // 释放锁
                        });
                    }
                }
            });
        }

        // --- 交互命令 ---

        /// <summary>
        /// 光谱仪启停切换逻辑（支持多机联机识别）
        /// </summary>
        [RelayCommand]
        private async Task ToggleSpectrometer()
        {
            // 新增防呆：如果设置里禁用了硬件，直接不让点
            var config = _jsonConfigService.Load();
            if (!config.IsSpectrometerEnabled)
            {
                StatusInfo = "请先在设置中启用光谱仪连接";
                return;
            }

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
                            // 判断是否已经连接，已连接则直接返回 true，未连接才执行 InitializeAsync
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

        [RelayCommand]
        private void OpenPlatform3DWindow()
        {
            // 1. 实例化窗口对象
            var window = new GD_ControlCenter_WPF.Views.Dialogs.Platform3DWindow();

            // 2. 实例化 ViewModel (手动注入关闭回调)
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.Platform3DViewModel(() => window.Close());

            // 3. 绑定 DataContext
            window.DataContext = vm;

            // 4. 指定 Owner 居中
            window.Owner = System.Windows.Application.Current.MainWindow;

            // 5. 弹出
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
        /// 自动范围调整：根据当前光谱数据的 Y 轴最高点自动缩放
        /// </summary>
        [RelayCommand]
        private void AutoRange()
        {
            // 发送自动缩放请求消息
            WeakReferenceMessenger.Default.Send(new AutoRangeRequestMessage());
        }

        /// <summary>
        /// 最大范围调整：恢复到全局预设的最大观察视野
        /// </summary>
        [RelayCommand]
        private void MaxRange()
        {
            WeakReferenceMessenger.Default.Send(new MaxRangeRequestMessage());
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

        // --- 自动点火与状态机逻辑 ---

        /// <summary>
        /// 硬件属性变更回调：监听高压电源电流及启停状态
        /// </summary>
        private void OnHardwareStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HighVoltageViewModel.MonitorCurrent) ||
                e.PropertyName == nameof(HighVoltageViewModel.IsRunning) ||
                e.PropertyName == nameof(PeristalticPumpViewModel.IsRunning))
            {
                CheckPlasmaState();
            }
        }

        /// <summary>
        /// 判定等离子体状态及是否意外熄火
        /// </summary>
        private void CheckPlasmaState()
        {
            // 防呆：如果任何一个硬件被手动关闭，立即重置状态并终止打火逻辑
            if (!_hvVM.IsRunning || !_pumpVM.IsRunning)
            {
                _isPlasmaStable = false;
                _reigniteAttemptCount = 0;
                return;
            }

            // 解析当前电流
            if (double.TryParse(_hvVM.MonitorCurrent, out double current))
            {
                // 阈值设定：假设电流 > 5.0mA 视为成功点燃并稳定 (可根据实际情况调整)
                if (current > 5.0)
                {
                    if (!_isPlasmaStable)
                    {
                        _isPlasmaStable = true;
                        _reigniteAttemptCount = 0; // 稳定后重置尝试次数
                        _isReigniting = false;
                        StatusInfo = "等离子体已稳定运行";
                    }
                }
                // 意外熄火判定：原本稳定 -> 突然跌落至 0 附近 (如 <= 1.0mA) -> 且不在重试打火过程中
                else if (current <= 1.0 && _isPlasmaStable && !_isReigniting)
                {
                    _isPlasmaStable = false;
                    _ = HandleUnexpectedExtinctionAsync(); // 触发异步重试序列
                }
            }
        }

        /// <summary>
        /// 异步执行自动打火序列
        /// </summary>
        private async Task HandleUnexpectedExtinctionAsync()
        {
            _isReigniting = true;

            // 检查配置是否允许自动打火
            var config = _jsonConfigService.Load();
            if (!config.IsAutoReigniteEnabled)
            {
                StatusInfo = "意外熄火 (未开启自动点火)";
                _isReigniting = false;
                return;
            }

            while (_reigniteAttemptCount < MaxReigniteAttempts)
            {
                _reigniteAttemptCount++;
                StatusInfo = $"意外熄火，准备尝试第 {_reigniteAttemptCount} 次自动点火...";

                // 缓冲延时：给蠕动泵进液/吹扫预留 2 秒时间
                await Task.Delay(2000);

                // 在延时期间，检查用户是否手动关机了，如果关机则直接中断
                if (!_hvVM.IsRunning || !_pumpVM.IsRunning)
                {
                    StatusInfo = "自动点火已取消 (硬件已手动关闭)";
                    _isReigniting = false;
                    return;
                }

                StatusInfo = $"正在执行第 {_reigniteAttemptCount} 次自动点火...";

                // 发送点火指令
                _generalDeviceService.Fire();

                // 等待点火后的电流攀升时间（此处假设给 3 秒反应时间）
                await Task.Delay(3000);

                // 再次检查电流判断是否点火成功
                if (double.TryParse(_hvVM.MonitorCurrent, out double current) && current > 5.0)
                {
                    // 点燃成功，退出循环，后续将交由 CheckPlasmaState 轮询接管变回 Stable 状态
                    _isReigniting = false;
                    return;
                }
            }

            // 如果走出了 while 循环，说明尝试达到最大次数依然失败
            StatusInfo = "多次自动点火失败，为保障安全已切断输出";

            // 安全切断硬件
            _hvVM.IsRunning = false;
            _pumpVM.IsRunning = false;
            _isReigniting = false;
        }
    }
}