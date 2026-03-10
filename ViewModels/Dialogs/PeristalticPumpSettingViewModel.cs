using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class PeristalticPumpSettingViewModel : ObservableObject
    {
        private readonly GeneralDeviceService _generalService;
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;
        private readonly bool _isRunning; // 当前运行状态

        [ObservableProperty]
        private int _targetSpeed; // int 类型天然限制了只能输入整数

        [ObservableProperty]
        private bool _isClockwise;

        [ObservableProperty]
        private string _directionHint = "前向";

        public PeristalticPumpSettingViewModel(GeneralDeviceService generalService, JsonConfigService configService, AppConfig currentConfig,
            bool isRunning, Action closeAction)
        {
            _generalService = generalService;
            _configService = configService;
            _isRunning = isRunning;
            _closeAction = closeAction;

            TargetSpeed = currentConfig.LastPumpSpeed;
            IsClockwise = currentConfig.IsPumpClockwise;
            UpdateDirectionHint(IsClockwise);
        }

        partial void OnIsClockwiseChanged(bool value) => UpdateDirectionHint(value);

        private void UpdateDirectionHint(bool value) => DirectionHint = value ? "前向" : "后向";

        [RelayCommand]
        private void Apply()
        {
            // 范围校验：蠕动泵限制为 0-100
            if (TargetSpeed < 0 || TargetSpeed > 100)
            {
                // 使用 MessageBox 提示（简单直接）或配合 Snackbar
                MessageBox.Show("转速设定超出有效范围 (0-100)", "输入错误",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var config = _configService.Load();
            config.LastPumpSpeed = TargetSpeed;
            config.IsPumpClockwise = IsClockwise;
            _configService.Save(config);

            // 如果正在运行，直接下发硬件指令，保持启动状态 (true)
            if (_isRunning)
            {
                _generalService.ControlPeristalticPump((short)TargetSpeed, IsClockwise, true);
            }

            _closeAction?.Invoke();
        }
    }
}