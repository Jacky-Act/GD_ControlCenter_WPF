using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private object _currentPage;
        [ObservableProperty] private double _dashboardHeight;

        // 直接由构造函数注入
        public ControlPanelViewModel ControlPanelVM { get; }
        public SettingsViewModel SettingsVM { get; }

        // 其他无依赖的纯 UI VM 依然可以手动 new
        public TimeSeriesViewModel TimeSeriesVM { get; } = new();
        public ElementConfigViewModel ElementConfigVM { get; } = new();
        public FittingCurveViewModel FittingCurveVM { get; } = new();
        public SampleMeasurementViewModel SampleMeasurementVM { get; } = new();

        private readonly HighVoltageService _hvService;
        private readonly JsonConfigService _configService;

        // 容器会自动把实例化好的对象塞进这里
        public MainViewModel(
            ControlPanelViewModel controlPanelVM,
            SettingsViewModel settingsVM,
            HighVoltageService hvService,
            JsonConfigService configService)
        {
            ControlPanelVM = controlPanelVM;
            SettingsVM = settingsVM;
            _hvService = hvService;
            _configService = configService;

            CurrentPage = ControlPanelVM;
        }

        public void SaveCurrentSettings()
        {
            var config = new AppConfig
            {
                LastHvVoltage = _hvService.Voltage,
                LastHvCurrent = _hvService.Current
            };
            _configService.Save(config);
        }
    }
}