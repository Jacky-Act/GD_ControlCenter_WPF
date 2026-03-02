using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
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
            DirectionText = config.IsPumpClockwise ? "前向" : "后向";
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

        ///// <summary>
        ///// 当点击右侧“启动/停止”按钮时触发
        ///// </summary>
        //partial void OnIsRunningChanged(bool value)
        //{
        //    // 每次切换开关时，重新读取最新的配置参数
        //    var config = _configService.Load();

        //    // 下发指令到硬件
        //    // 参数：转速(short), 方向(bool), 是否启动(bool)
        //    _generalService.ControlPeristalticPump(
        //        (short)config.LastPumpSpeed,
        //        config.IsPumpClockwise,
        //        value
        //    );
        //}
    }
}