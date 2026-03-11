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

            // 读取上次的配置，若无则给默认值
            SampleCount = currentConfig.LastSampleCount > 0 ? currentConfig.LastSampleCount : 11;
            SampleInterval = currentConfig.LastSampleInterval > 0 ? currentConfig.LastSampleInterval : 1;
        }

        // --- 新增：采样次数增减命令 ---
        [RelayCommand]
        private void IncreaseSampleCount()
        {
            if (SampleCount < 10000) SampleCount++;
        }

        [RelayCommand]
        private void DecreaseSampleCount()
        {
            if (SampleCount > 1) SampleCount--;
        }

        // --- 新增：采样间隔增减命令 ---
        [RelayCommand]
        private void IncreaseSampleInterval()
        {
            if (SampleInterval < 3600) SampleInterval++;
        }

        [RelayCommand]
        private void DecreaseSampleInterval()
        {
            if (SampleInterval > 0) SampleInterval--;
        }

        [RelayCommand]
        private void Apply()
        {
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

            _closeAction?.Invoke();
        }
    }
}