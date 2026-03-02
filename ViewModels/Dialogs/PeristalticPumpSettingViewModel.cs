using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class PeristalticPumpSettingViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;

        [ObservableProperty]
        private int _targetSpeed;

        [ObservableProperty]
        private bool _isClockwise;

        public PeristalticPumpSettingViewModel(JsonConfigService configService, AppConfig currentConfig, Action closeAction)
        {
            _configService = configService;
            _closeAction = closeAction;

            // 加载当前保存的配置
            TargetSpeed = currentConfig.LastPumpSpeed;
            IsClockwise = currentConfig.IsPumpClockwise;
        }

        [RelayCommand]
        private void Apply()
        {
            // 仅执行保存逻辑，不发送硬件指令
            var config = _configService.Load();
            config.LastPumpSpeed = TargetSpeed;
            config.IsPumpClockwise = IsClockwise;
            _configService.Save(config);

            // 关闭窗口
            _closeAction?.Invoke();
        }
    }
}