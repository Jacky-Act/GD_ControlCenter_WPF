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

        [ObservableProperty]
        private string _pumpFlowRateK;

        [ObservableProperty]
        private string _pumpFlowRateB;

        // 【新增】用于在界面实时展示的计算流速
        [ObservableProperty]
        private double _calculatedFlowRate;

        public PeristalticPumpSettingViewModel(GeneralDeviceService generalService, JsonConfigService configService, AppConfig currentConfig,
            bool isRunning, Action closeAction)
        {
            _generalService = generalService;
            _configService = configService;
            _isRunning = isRunning;
            _closeAction = closeAction;

            TargetSpeed = currentConfig.LastPumpSpeed;
            IsClockwise = currentConfig.IsPumpClockwise;
            PumpFlowRateK = currentConfig.PumpFlowRateK.ToString();
            PumpFlowRateB = currentConfig.PumpFlowRateB.ToString(); 

            // 【新增】窗口打开时，进行一次初始计算
            UpdateCalculatedFlowRate();

            UpdateDirectionHint(IsClockwise);
        }

        partial void OnIsClockwiseChanged(bool value) => UpdateDirectionHint(value);

        private void UpdateDirectionHint(bool value) => DirectionHint = value ? "前向" : "后向";

        // 【新增】利用 MVVM Toolkit 拦截器，当转速、K 或 B 任意一个发生变化时，立刻触发重新计算
        partial void OnTargetSpeedChanged(int value) => UpdateCalculatedFlowRate();
        partial void OnPumpFlowRateKChanged(string value) => UpdateCalculatedFlowRate();
        partial void OnPumpFlowRateBChanged(string value) => UpdateCalculatedFlowRate();

        // 【新增】核心计算逻辑
        private void UpdateCalculatedFlowRate()
        {
            // 尝试解析字符串，如果用户输入为空或非法字符，默认当作 0 处理，防止程序崩溃
            double k = double.TryParse(PumpFlowRateK, out double parsedK) ? parsedK : 0;
            double b = double.TryParse(PumpFlowRateB, out double parsedB) ? parsedB : 0;

            CalculatedFlowRate = Math.Round(k * TargetSpeed + b, 2);
        }

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
            // 【修改】保存时将字符串转换为 double 写入配置
            config.PumpFlowRateK = double.TryParse(PumpFlowRateK, out double finalK) ? finalK : 1.0;
            config.PumpFlowRateB = double.TryParse(PumpFlowRateB, out double finalB) ? finalB : 0.0;
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