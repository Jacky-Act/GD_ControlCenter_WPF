using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private readonly JsonConfigService _configService = new();
        private readonly ProtocolService _protocolService;
        private readonly BatteryService _batteryService;
        private readonly HighVoltageService _hvService;

        public MainViewModel()
        {

            // 1. 实例化基础服务（实际项目中建议使用依赖注入容器）
            var serialService = new SerialPortService();
            var generalService = new GeneralDeviceService(serialService);
            _hvService = new HighVoltageService(serialService);

            // 2. 设置测试用的串口号和波特率
            // 假设你的硬件或虚拟串口连接在 COM2，波特率为 9600
            serialService.Open("COM2", 9600);

            // 2. 加载历史配置
            var config = _configService.Load();

            // 3. 将历史值赋给服务层或准备下发硬件
            _hvService.SetHighVoltage(config.LastHvVoltage, config.LastHvCurrent);

            _protocolService = new ProtocolService();
            _batteryService = new BatteryService(serialService);
            _batteryService.Start();

            // 2. 将服务传给 ControlPanelVM
            ControlPanelVM = new ControlPanelViewModel(generalService, _batteryService, _hvService, _configService);

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
