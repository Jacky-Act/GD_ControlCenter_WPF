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
            if (IsBusy) return; // 运行期间禁止打开设置窗口

            var currentConfig = _configService.Load();
            var window = new SyringePumpSettingWindow();

            var vm = new SyringePumpSettingViewModel(_configService, currentConfig, () =>
            {
                window.Close();
                UpdateUIFromConfig();
            });

            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 当点击右侧“启动”按钮时触发（预留逻辑）
        /// </summary>
        //protected override async void OnPropertyChanged(PropertyChangedEventArgs e)
        //{
        //    base.OnPropertyChanged(e);

        //    // 1. 检查是否是运行状态改变，且当前状态为“启动(True)”
        //    if (e.PropertyName == nameof(IsRunning) && IsRunning)
        //    {
        //        // 2. 读取最新配置
        //        var config = _configService.Load();
        //        int durationMs = config.LastSyringeDistance;

        //        // 3. 调用硬件服务下发指令
        //        // 这里的距离值 0-3000 被逻辑视为 0-3000 毫秒
        //        _generalService.ControlSyringePump(config.IsSyringeOutput, (short)durationMs);

        //        // 4. 异步等待对应的时间，此时界面按钮保持“停止”文字和选中颜色
        //        if (durationMs > 0)
        //        {
        //            await Task.Delay(durationMs);
        //        }

        //        // 5. 时间到达后，复位按钮状态为 False (界面变回“启动”)
        //        IsRunning = false;
        //    }
        //}

        /// <summary>
        /// 自动化序列：一键完成 抽水 -> 延时 -> 推水 -> 延时 -> 恢复待机
        /// </summary>
        protected override async void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(IsRunning) && IsRunning)
            {
                IsBusy = true; // 锁定按钮，使其置灰不可点击

                var config = _configService.Load();
                short targetDistance = (short)config.LastSyringeDistance;
                bool initialPort = config.IsSyringeOutput;

                // 计算延时：(行程 / 3000) * 4500ms + 1000ms 固定缓冲
                int delayMs = (int)((targetDistance / 3000.0) * 4500) + 1000;

                try
                {
                    // 阶段 1：抽水
                    ButtonText = "抽";
                    _generalService.ControlSyringePump(initialPort, targetDistance);
                    await Task.Delay(delayMs);

                    // 阶段 2：推水（距离归0，自动切换到对立口）
                    ButtonText = "推";
                    _generalService.ControlSyringePump(!initialPort, 0);
                    await Task.Delay(delayMs);
                }
                finally
                {
                    // 阶段 3：恢复状态
                    ButtonText = "启动";
                    IsRunning = false;
                    IsBusy = false; // 解除锁定，按钮恢复可用
                }
            }
        }
    }
}