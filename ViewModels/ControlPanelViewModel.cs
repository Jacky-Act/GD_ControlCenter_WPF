using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Timers;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class ControlPanelViewModel : ObservableObject
    {
        // 设备卡片集合，前端将绑定到此属性
        [ObservableProperty]
        private ObservableCollection<DeviceBaseViewModel> _devices = new();

        private readonly System.Timers.Timer _dataTimer;
        private readonly Random _random = new();

        public ControlPanelViewModel()
        {
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
            Devices.Add(new HighVoltageViewModel());

            // 初始化蠕动泵并加入集合
            Devices.Add(new PeristalticPumpViewModel());

            // 新增：用于卡片 3 和 卡片 4 的占位 ViewModel（假设已创建对应类或使用基类占位）
            Devices.Add(new SyringePumpViewModel());     // Index 2 (注射泵)

            // 后续增加 3D 平台或其他设备只需在此 Add 即可
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            foreach (var device in Devices)
            {
                // 只有当设备处于“启动”状态时才模拟数据变化
                if (device.IsRunning)
                {
                    if (device is HighVoltageViewModel)
                    {
                        // 模拟电压波动 (15.0 - 15.5 V)
                        double voltage = 15.0 + _random.NextDouble() * 0.5;
                        device.DisplayValue = $"{voltage:F1} V";
                    }
                    else if (device is PeristalticPumpViewModel)
                    {
                        // 模拟转速波动 (100 - 105 RPM)
                        int rpm = 100 + _random.Next(0, 6);
                        device.DisplayValue = $"{rpm} RPM";
                    }
                }
                else
                {
                    // 未启动时显示初始值
                    device.DisplayValue = device is HighVoltageViewModel ? "0.0 V" : "0 RPM";
                }
            }
        }
    }
}