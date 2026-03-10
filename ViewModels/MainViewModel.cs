using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private object _currentPage; // 当前显示的页面对象

        [ObservableProperty]
        private double _dashboardHeight; // 用于绑定到界面的卡片区高度

        public ControlPanelViewModel ControlPanelVM { get; }
        public TimeSeriesViewModel TimeSeriesVM { get; } = new();
        public ElementConfigViewModel ElementConfigVM { get; } = new();
        public FittingCurveViewModel FittingCurveVM { get; } = new();
        public SampleMeasurementViewModel SampleMeasurementVM { get; } = new();
        public SpatialDistributionViewModel SpatialDistributionVM { get; } = new();
        public SettingsViewModel SettingsVM { get; }

        private readonly JsonConfigService _configService = new();
        private readonly ProtocolService _protocolService;
        private readonly BatteryService _batteryService;
        private readonly HighVoltageService _hvService;

        public MainViewModel()
        {
            // 1. 加载历史配置（前置，以供服务层使用）
            var config = _configService.Load();

            // 2. 实例化基础服务（将 _configService 注入到串口服务中）
            var serialService = new SerialPortService(_configService);
            var generalService = new GeneralDeviceService(serialService);
            _hvService = new HighVoltageService(serialService);

            // 3. 将历史值赋给服务层或准备下发硬件
            _hvService.SetHighVoltage(config.LastHvVoltage, config.LastHvCurrent);

            _protocolService = new ProtocolService();
            _batteryService = new BatteryService(serialService);
            _batteryService.Start();

            // 4. 将服务传给 ViewModel
            ControlPanelVM = new ControlPanelViewModel(generalService, _batteryService, _hvService, _configService);

            // 5. 实例化 SettingsVM，此过程会自动触发串口的寻找与自动连接
            SettingsVM = new SettingsViewModel(serialService);

            // 默认显示仪表台
            CurrentPage = ControlPanelVM;
        }

        // 示例：保存当前状态的方法
        public void SaveCurrentSettings()
        {
            var config = new AppConfig
            {
                LastHvVoltage = _hvService.Voltage, // 或者从设置变量中获取
                LastHvCurrent = _hvService.Current
            };
            _configService.Save(config);
        }
    }
}