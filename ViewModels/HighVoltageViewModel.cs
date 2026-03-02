using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class HighVoltageViewModel : DeviceBaseViewModel
    {
        private readonly HighVoltageService _hvService;
        private readonly JsonConfigService _configService; // 新增配置服务引用

        // --- 1. 定义 UI 专用属性 ---
        [ObservableProperty]
        private string _setVoltage = "0 V";

        [ObservableProperty]
        private string _setCurrent = "0 mA";

        [ObservableProperty]
        private string _monitorVoltage = "0 V";

        [ObservableProperty]
        private string _monitorCurrent = "0 mA";

        [ObservableProperty]
        private string _statusText = "离线";

        // --- 2. 构造函数注入 Service ---
        public HighVoltageViewModel(HighVoltageService hvService, JsonConfigService configService)
        {
            _hvService = hvService;
            _configService = configService;

            DisplayName = "高压电源";
            ThemeColor = Brushes.DodgerBlue;

            // 订阅 Service 的属性变化事件
            _hvService.PropertyChanged += OnServicePropertyChanged;

            // 初始化同步一次状态
            UpdateUIProperties();
        }

        // --- 3. 核心逻辑：数据同步 ---
        private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只要 Service 的数值变了，就更新 ViewModel 的 UI 属性
            // 使用 Dispatcher 确保在 UI 线程更新（如果 Service 是在后台线程触发的）
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateUIProperties();
            });
        }

        private void UpdateUIProperties()
        {
            // 1. 同步监控值（来自 Service）
            MonitorVoltage = $"{_hvService.Voltage} V";
            MonitorCurrent = $"{_hvService.Current} mA";
            StatusText = _hvService.IsOnline ? "在线" : "离线";

            // 2. 同步设定值（来自配置文件）
            var config = _configService.Load();
            SetVoltage = $"{config.LastHvVoltage} V";
            SetCurrent = $"{config.LastHvCurrent} mA";

            // 更新基类的 DisplayValue (如果通用卡片还在使用这个属性的话)
            DisplayValue = MonitorVoltage;
        }

        /// <summary>
        /// 点击卡片左侧触发：弹出设置窗口
        /// </summary>
        protected override void ExecuteOpenDetail()
        {
            // 1. 获取当前最新的本地配置
            var currentConfig = _configService.Load();

            // 2. 创建弹窗实例
            var window = new HvSettingWindow();

            // 3. 创建弹窗的 ViewModel 并注入依赖
            // 传入 Action 用于在点击“应用”时关闭窗口
            var vm = new HvSettingViewModel(
                       _hvService,
                       _configService,
                       currentConfig,
                       () =>
                       {
                           window.Close();
                           // 重点：弹窗关闭后立即手动刷新一次 UI，确保卡片显示最新保存的值
                           UpdateUIProperties();
                       }
                   );

            // 4. 绑定与显示
            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow; // 居中于主窗口
            window.ShowDialog(); // 模态显示
        }
    }
}