using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private object _currentPage;
        [ObservableProperty] private double _dashboardHeight;

        // ================= 所有的子 VM 都由容器注入，保持全局单例 =================
        public ControlPanelViewModel ControlPanelVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public TimeSeriesViewModel TimeSeriesVM { get; }
        public ElementConfigViewModel ElementConfigVM { get; }
        public SampleSequenceViewModel SampleSequenceVM { get; }
        public AnalysisWorkstationViewModel AnalysisWorkstationVM { get; }

        public DataProcessingViewModel DataProcessingVM { get; } = new();
        public ReportGenerationViewModel ReportGenerationVM { get; } = new();


        private readonly HighVoltageService _hvService;
        private readonly JsonConfigService _configService;
        private readonly ProtocolService _protocolService;

        // 核心修复：在这个超级庞大的构造函数里，直接让 IoC 容器把所有单例喂进来！
        public MainViewModel(
            ControlPanelViewModel controlPanelVM,
            SettingsViewModel settingsVM,
            TimeSeriesViewModel timeSeriesVM,
            ElementConfigViewModel elementConfigVM,
            SampleSequenceViewModel sampleSequenceVM,
            AnalysisWorkstationViewModel analysisWorkstationVM,
            HighVoltageService hvService,
            JsonConfigService configService,
            ProtocolService protocolService)
        {
            ControlPanelVM = controlPanelVM;
            SettingsVM = settingsVM;
            TimeSeriesVM = timeSeriesVM;
            ElementConfigVM = elementConfigVM;
            SampleSequenceVM = sampleSequenceVM;
            AnalysisWorkstationVM = analysisWorkstationVM;

            _hvService = hvService;
            _configService = configService;
            _protocolService = protocolService;

            // 初始页面定为仪表台
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