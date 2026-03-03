using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class SyringePumpSettingViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;

        [ObservableProperty]
        private int _targetDistance;

        [ObservableProperty]
        private bool _isOutput;

        [ObservableProperty]
        private string _directionHint = "输出";

        public SyringePumpSettingViewModel(JsonConfigService configService, AppConfig currentConfig, Action closeAction)
        {
            _configService = configService;
            _closeAction = closeAction;

            // 从配置中加载初始化值
            TargetDistance = currentConfig.LastSyringeDistance;
            IsOutput = currentConfig.IsSyringeOutput;
            UpdateDirectionHint(IsOutput);
        }

        // 监听方向切换，更新 UI 提示文字
        partial void OnIsOutputChanged(bool value) => UpdateDirectionHint(value);

        private void UpdateDirectionHint(bool value) => DirectionHint = value ? "输出" : "输入";

        [RelayCommand]
        private void Apply()
        {
            // 1. 范围校验：注射泵限制为 0 - 3000
            if (TargetDistance < 0 || TargetDistance > 3000)
            {
                MessageBox.Show("移动距离设定超出有效范围 (0-3000)", "输入错误",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 持久化保存至配置文件
            var config = _configService.Load();
            config.LastSyringeDistance = TargetDistance;
            config.IsSyringeOutput = IsOutput;
            _configService.Save(config);

            // 3. 执行关闭窗口回调
            _closeAction?.Invoke();
        }
    }
}