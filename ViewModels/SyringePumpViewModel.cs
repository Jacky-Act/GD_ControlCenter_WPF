using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
using System.Windows;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class SyringePumpViewModel : DeviceBaseViewModel
    {
        private readonly GeneralDeviceService _generalService;
        private readonly JsonConfigService _configService;

        [ObservableProperty]
        private string _settingDistance = "0";

        [ObservableProperty]
        private string _directionText = "输出";

        public SyringePumpViewModel(GeneralDeviceService generalService, JsonConfigService configService)
        {
            _generalService = generalService;
            _configService = configService;

            DisplayName = "注射泵";
            ThemeColor = Brushes.Teal;

            // 初始化同步一次状态
            UpdateUIFromConfig();
        }

        /// <summary>
        /// 从配置文件同步 UI 显示
        /// </summary>
        private void UpdateUIFromConfig()
        {
            var config = _configService.Load();
            SettingDistance = $"{config.LastSyringeDistance}"; // 纯数字显示
            DirectionText = config.IsSyringeOutput ? "输出" : "输入";
        }

        /// <summary>
        /// 点击卡片左侧触发：弹出设置窗口
        /// </summary>
        protected override void ExecuteOpenDetail()
        {
            var currentConfig = _configService.Load();
            var window = new SyringePumpSettingWindow();

            var vm = new SyringePumpSettingViewModel(
                _configService,
                currentConfig,
                () =>
                {
                    window.Close();
                    UpdateUIFromConfig(); // 关闭窗口后立即刷新卡片显示
                }
            );

            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        ///// <summary>
        ///// 当点击右侧“启动”按钮时触发（预留逻辑）
        ///// </summary>
        //partial void OnIsRunningChanged(bool value)
        //{
        //    if (value)
        //    {
        //        var config = _configService.Load();
        //        // 调用硬件服务：距离范围 0-3000
        //        _generalService.ControlSyringePump(config.IsSyringeOutput, (short)config.LastSyringeDistance);

        //        // 执行完即刻复位按钮状态（因为注射泵通常是单次执行指令）
        //        IsRunning = false;
        //    }
        //}
    }
}