using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class HvSettingViewModel : ObservableObject
    {
        private readonly HighVoltageService _hvService;
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;
        private readonly bool _isRunning; // 保存当前运行状态

        // 绑定到滑动条和文本框的电压值 (0-1500V)
        [ObservableProperty]
        private int _targetVoltage;

        // 绑定到滑动条和文本框的电流值 (0-100mA)
        [ObservableProperty]
        private int _targetCurrent;

        public HvSettingViewModel(HighVoltageService hvService, JsonConfigService configService, AppConfig currentConfig,
            bool isRunning, Action closeAction)
        {
            _hvService = hvService;
            _configService = configService;
            _isRunning = isRunning;
            _closeAction = closeAction;

            // 初始化时，从读取到的本地配置中加载上次保存的数值
            TargetVoltage = currentConfig.LastHvVoltage;
            TargetCurrent = currentConfig.LastHvCurrent;
        }

        /// <summary>
        /// 执行“应用”逻辑
        /// </summary>
        [RelayCommand]
        private void Apply()
        {
            // 1. 范围校验
            if (TargetVoltage < 0 || TargetVoltage > 1500)
            {
                MessageBox.Show("电压设定值超出范围 (0-1500 V)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TargetCurrent < 0 || TargetCurrent > 100)
            {
                MessageBox.Show("电流设定值超出范围 (0-100 mA)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 持久化保存到配置文件，供后续“启动”按钮读取
            var config = _configService.Load();
            config.LastHvVoltage = TargetVoltage;
            config.LastHvCurrent = TargetCurrent;
            _configService.Save(config);

            // 3. 如果正在运行，直接向硬件发送新参数
            if (_isRunning)
            {
                _hvService.SetHighVoltage(TargetVoltage, TargetCurrent);
            }

            // 4. 执行关闭回调
            _closeAction?.Invoke();
        }
    }
}