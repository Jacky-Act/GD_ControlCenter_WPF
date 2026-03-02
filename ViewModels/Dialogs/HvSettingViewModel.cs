using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class HvSettingViewModel : ObservableObject
    {
        private readonly HighVoltageService _hvService;
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;

        // 绑定到滑动条和文本框的电压值 (0-1500V)
        [ObservableProperty]
        private int _targetVoltage;

        // 绑定到滑动条和文本框的电流值 (0-100mA)
        [ObservableProperty]
        private int _targetCurrent;

        public HvSettingViewModel(
            HighVoltageService hvService,
            JsonConfigService configService,
            AppConfig currentConfig,
            Action closeAction)
        {
            _hvService = hvService;
            _configService = configService;
            _closeAction = closeAction;

            // 初始化时，从读取到的本地配置中加载上次保存的数值
            TargetVoltage = currentConfig.LastHvVoltage;
            TargetCurrent = currentConfig.LastHvCurrent;
        }

        /// <summary>
        /// 执行“应用并保存”逻辑
        /// </summary>
        [RelayCommand]
        private void Apply()
        {
            // 1. 下发硬件指令：立即更改电源输出
            _hvService.SetHighVoltage(TargetVoltage, TargetCurrent);

            // 2. 持久化存储：更新本地 JSON 配置文件
            var config = _configService.Load();
            config.LastHvVoltage = TargetVoltage;
            config.LastHvCurrent = TargetCurrent;
            _configService.Save(config);

            // 3. 执行关闭窗口的回调
            _closeAction?.Invoke();
        }
    }
}