using CommunityToolkit.Mvvm.ComponentModel;
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
        public LogViewModel LogVM { get; } = new();
        public SpatialDistributionViewModel SpatialDistributionVM { get; } = new();

        public MainViewModel()
        {
            // 1. 实例化基础服务（实际项目中建议使用依赖注入容器）
            var serialService = new SerialPortService();
            var generalService = new GeneralDeviceService(serialService);

            // 2. 设置测试用的串口号和波特率
            // 假设你的硬件或虚拟串口连接在 COM2，波特率为 9600
            serialService.Open("COM2", 9600);

            // 3. 实例化协议服务（它会自动开始监听 serialService 发出的消息）
            var protocolService = new ProtocolService();

            // 2. 将服务传给 ControlPanelVM
            ControlPanelVM = new ControlPanelViewModel(generalService);

            // 默认显示仪表台
            CurrentPage = ControlPanelVM;
        }
    }
}
