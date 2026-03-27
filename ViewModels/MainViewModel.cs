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
        public TimeSeriesViewModel TimeSeriesVM { get; }

        // 其他无依赖的纯 UI VM 依然可以手动 new
        public ElementConfigViewModel ElementConfigVM { get; } = new();
        public FittingCurveViewModel FittingCurveVM { get; } = new();
        public SampleMeasurementViewModel SampleMeasurementVM { get; } = new();

        // ================== 新增：流动注射的 VM 属性 ==================
        public FlowInjectionViewModel FlowInjectionVM { get; } = new();
        // ==============================================================

        // ================== 新增：样品序列的 VM 属性 ==================
        public SampleSequenceViewModel SampleSequenceVM { get; } = new();

        private readonly HighVoltageService _hvService;
        private readonly JsonConfigService _configService;
        private readonly ProtocolService _protocolService;

        // 容器会自动把实例化好的对象塞进这里
        public MainViewModel(
            ControlPanelViewModel controlPanelVM,
            SettingsViewModel settingsVM,
            TimeSeriesViewModel timeSeriesVM,
            HighVoltageService hvService,
            JsonConfigService configService,
            ProtocolService protocolService)
        {
            ControlPanelVM = controlPanelVM;
            SettingsVM = settingsVM;
            TimeSeriesVM = timeSeriesVM;
            _hvService = hvService;
            _configService = configService;
            _protocolService = protocolService;

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