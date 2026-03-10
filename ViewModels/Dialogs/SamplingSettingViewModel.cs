using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class SamplingSettingViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;

        [ObservableProperty]
        private int _sampleCount;

        [ObservableProperty]
        private int _sampleInterval;

        public SamplingSettingViewModel(JsonConfigService configService, AppConfig currentConfig, Action closeAction)
        {
            _configService = configService;
            _closeAction = closeAction;

            // 读取上次的配置，若无则给默认值（需确保 AppConfig 类中有这两个属性）
            SampleCount = currentConfig.LastSampleCount > 0 ? currentConfig.LastSampleCount : 11;
            SampleInterval = currentConfig.LastSampleInterval > 0 ? currentConfig.LastSampleInterval : 1;
        }

        [RelayCommand]
        private void Apply()
        {
            // 简单的数据校验（NumericUpDown 的 Minimum 属性在 UI 层面已做了基础拦截）
            if (SampleCount < 1)
            {
                MessageBox.Show("采样次数必须至少为 1", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SampleInterval <= 0)
            {
                MessageBox.Show("采样间隔必须大于 0", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 保存配置
            var config = _configService.Load();
            config.LastSampleCount = SampleCount;
            config.LastSampleInterval = SampleInterval;
            _configService.Save(config);

            // 执行关闭窗口的操作
            _closeAction?.Invoke();
        }
    }
}
