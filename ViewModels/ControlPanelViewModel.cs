using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Timers;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class ControlPanelViewModel : ObservableObject
    {
        // --- 1. 数据模型引用 (Model) ---
        [ObservableProperty]
        private SpectrometerConfig _specModel = new();

        // --- 2. 设备卡片集合 ---
        [ObservableProperty]
        private ObservableCollection<DeviceBaseViewModel> _devices = new();

        // --- 3. 布局控制属性 ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullScreenButtonText))]
        private double _dashboardHeight; // 上方卡片区高度
        private double _originalHeight; // 初始计算高度备份

        [ObservableProperty]
        private double _paramHeaderHeight = 35; // 参数栏高度

        [ObservableProperty]
        private string _fullScreenButtonText = "大图显示";

        [ObservableProperty]
        private string _statusInfo = "光谱仪就绪";

        private readonly System.Timers.Timer _dataTimer;
        private readonly Random _random = new();

        // 2. 注入通用设备服务
        private readonly JsonConfigService _jsonConfigService;
        private readonly GeneralDeviceService _generalDeviceService;
        private readonly HighVoltageService _highVoltageService;

        [ObservableProperty]
        private BatteryService _batterySvc;

        // 3. 定义转向阀状态属性
        [ObservableProperty]
        private bool _isSteeringValveActive;


        public ControlPanelViewModel(GeneralDeviceService generalDeviceService, BatteryService batteryService, 
            HighVoltageService highVoltageService, JsonConfigService jsonConfigService)
        {
            _generalDeviceService = generalDeviceService;
            _batterySvc = batteryService; // 接入电池服务
            _highVoltageService = highVoltageService; // 保存引用
            _jsonConfigService = jsonConfigService; // <--- 保存引用
            // 启动电池监控
            _batterySvc.Start();

            InitializeDevices();

            // 设置定时器，每 500ms 更新一次数据
            _dataTimer = new System.Timers.Timer(500);
            _dataTimer.Elapsed += OnTimerElapsed;
            _dataTimer.AutoReset = true;
            _dataTimer.Enabled = true;
        }

        private void InitializeDevices()
        {
            // 初始化高压电源并加入集合
            // 传入两个 service
            Devices.Add(new HighVoltageViewModel(_highVoltageService, _jsonConfigService));

            // 初始化蠕动泵并加入集合
            Devices.Add(new PeristalticPumpViewModel(_generalDeviceService, _jsonConfigService));

            // 新增：用于卡片 3 和 卡片 4 的占位 ViewModel（假设已创建对应类或使用基类占位）
            Devices.Add(new SyringePumpViewModel());     // Index 2 (注射泵)

            // 后续增加 3D 平台或其他设备只需在此 Add 即可
        }

        /// <summary>
        /// 外部（如 MainWindow）初始化高度时调用
        /// </summary>
        public void SetInitialHeight(double height)
        {
            DashboardHeight = height;
            _originalHeight = height;
            // 动态设置参数栏高度，例如锁定在卡片区高度的 1/6 左右
            ParamHeaderHeight = Math.Max(35, height * 0.15);
        }

        // --- 4. 交互命令 ---

        [RelayCommand]
        private void ToggleFullScreen()
        {
            if (DashboardHeight > 0)
            {
                DashboardHeight = 0; // 隐藏卡片区实现大图模式
                FullScreenButtonText = "还原显示";
                StatusInfo = "当前处于全屏监控模式";
            }
            else
            {
                DashboardHeight = _originalHeight; // 还原高度
                FullScreenButtonText = "大图显示";
                StatusInfo = "光谱仪就绪";
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            foreach (var device in Devices)
            {
                // 如果是高压电源，由于它内部已经绑定了 Service，这里不再手动赋值 DisplayValue
                if (device is HighVoltageViewModel)
                {
                    // 逻辑已迁移至 HighVoltageViewModel 内部，此处留空或处理非数据 UI 逻辑
                    continue;
                }

                // 只有当设备处于“启动”状态时才模拟数据变化（针对尚未重构的设备）
                if (device.IsRunning)
                {
                    if (device is PeristalticPumpViewModel)
                    {
                        // 暂时保留蠕动泵模拟，直到你完成 PeristalticPumpService 的对接
                        int rpm = 100 + _random.Next(0, 6);
                        device.DisplayValue = $"{rpm} RPM";
                    }
                }
                else
                {
                    // 未启动时显示初始值
                    if (device is PeristalticPumpViewModel) device.DisplayValue = "0 RPM";
                }
            }
        }

        // 5. 核心：当 UI 切换开关时触发此方法
        partial void OnIsSteeringValveActiveChanged(bool value)
        {
            // 下发硬件指令：true 对应通道2，false 对应通道1
            _generalDeviceService.ControlSteeringValve(value);

            // 埋点：后续在此处添加日志写入逻辑
            // LogService.Write($"转向阀切换至通道 {(value ? 2 : 1)}");
        }

        // 点击点火按钮时触发此方法
        [RelayCommand]
        private void Fire()
        {
            // 调用通用设备服务的点火接口
            _generalDeviceService.Fire();

            // 可以在此处更新状态栏信息，给用户反馈
            StatusInfo = "正在执行点火序列...";

            // 日志埋点：记录点火操作
            // LogService.Write("手动触发点火指令");
        }
    }
}