using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
using System.ComponentModel;
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

        /// <summary>
        /// 当点击右侧“启动”按钮时触发（预留逻辑）
        /// </summary>
        protected override async void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            // 1. 检查是否是运行状态改变，且当前状态为“启动(True)”
            if (e.PropertyName == nameof(IsRunning) && IsRunning)
            {
                // 2. 读取最新配置
                var config = _configService.Load();
                int durationMs = config.LastSyringeDistance;

                // 3. 调用硬件服务下发指令
                // 这里的距离值 0-3000 被逻辑视为 0-3000 毫秒
                _generalService.ControlSyringePump(config.IsSyringeOutput, (short)durationMs);

                // 4. 异步等待对应的时间，此时界面按钮保持“停止”文字和选中颜色
                if (durationMs > 0)
                {
                    await Task.Delay(durationMs);
                }

                // 5. 时间到达后，复位按钮状态为 False (界面变回“启动”)
                IsRunning = false;
            }
        }
    }
}