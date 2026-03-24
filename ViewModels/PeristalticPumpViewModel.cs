using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
using System.ComponentModel;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class PeristalticPumpViewModel : DeviceBaseViewModel
    {
        private readonly GeneralDeviceService _generalService;
        private readonly JsonConfigService _configService;

        // 保留原有的属性防报错
        [ObservableProperty]
        private string _settingSpeed = "0";

        [ObservableProperty]
        private string _directionText = "前向";

        // 【新增】：用于主卡片显示的流速
        [ObservableProperty]
        private double _currentFlowRate;

        public PeristalticPumpViewModel(GeneralDeviceService generalService, JsonConfigService configService)
        {
            _generalService = generalService;
            _configService = configService;
            DisplayName = "蠕动泵";
            UpdateUIFromConfig();
        }

        private void UpdateUIFromConfig()
        {
            var config = _configService.Load();
            SettingSpeed = $"{config.LastPumpSpeed}";
            DirectionText = config.IsPumpClockwise ? "后向" : "前向";

            // 【核心修改】：计算流速，前端卡片会自动刷新显示这个值
            CurrentFlowRate = Math.Round(config.PumpFlowRateK * config.LastPumpSpeed + config.PumpFlowRateB, 2);
        }

        protected override void ExecuteOpenDetail()
        {
            var currentConfig = _configService.Load();
            var window = new PeristalticPumpSettingWindow();

            var vm = new PeristalticPumpSettingViewModel(_generalService, _configService, currentConfig, this.IsRunning, () =>
            {
                window.Close();
                // 弹窗关闭时，重新读取配置并计算最新的流速
                UpdateUIFromConfig();
            }
            );

            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(IsRunning))
            {
                var config = _configService.Load();
                _generalService.ControlPeristalticPump((short)config.LastPumpSpeed, config.IsPumpClockwise, IsRunning);
            }
        }
    }
}