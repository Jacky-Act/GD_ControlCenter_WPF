using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class PeristalticPumpViewModel : DeviceBaseViewModel
    {
        private readonly GeneralDeviceService _generalService;
        private readonly JsonConfigService _configService;

        [ObservableProperty]
        private string _settingSpeed = "0";

        [ObservableProperty]
        private string _directionText = "前向";

        public PeristalticPumpViewModel(GeneralDeviceService generalService, JsonConfigService configService)
        {
            _generalService = generalService;
            _configService = configService;

            DisplayName = "蠕动泵";
            ThemeColor = Brushes.MediumPurple; // 设置一个识别度高的颜色

            // 初始化显示
            UpdateUIFromConfig();
        }

        /// <summary>
        /// 从配置文件同步 UI 显示
        /// </summary>
        private void UpdateUIFromConfig()
        {
            var config = _configService.Load();
            SettingSpeed = $"{config.LastPumpSpeed}";
            DirectionText = config.IsPumpClockwise ? "后向" : "前向";
        }

        /// <summary>
        /// 点击卡片左侧：弹出设置窗口
        /// </summary>
        protected override void ExecuteOpenDetail()
        {
            var currentConfig = _configService.Load();
            var window = new PeristalticPumpSettingWindow();

            var vm = new PeristalticPumpSettingViewModel(
                _configService,
                currentConfig,
                () =>
                {
                    window.Close();
                    UpdateUIFromConfig(); // 关闭窗口后刷新卡片上的预设值显示
                }
            );

            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 当点击右侧“启动/停止”按钮时触发
        /// </summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            // 检查是否是 IsRunning 属性发生了变化
            if (e.PropertyName == nameof(IsRunning))
            {
                // 每次切换开关时，重新读取最新的配置参数
                var config = _configService.Load();

                // 下发指令到硬件
                _generalService.ControlPeristalticPump(
                    (short)config.LastPumpSpeed,
                    config.IsPumpClockwise,
                    IsRunning // 这里的 IsRunning 就是改变后的值
                );
            }
        }
    }
}