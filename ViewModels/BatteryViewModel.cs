using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Services;
using System;

namespace GD_ControlCenter_WPF.ViewModels
{
    /// <summary>
    /// 电池视图模型：负责将底层 BatteryService 的状态暴露给 UI 绑定。
    /// </summary>
    public partial class BatteryViewModel : ObservableObject
    {
        private readonly BatteryService _batteryService;

        [ObservableProperty]
        private int _percentage;

        [ObservableProperty]
        private bool _isOnline;

        [ObservableProperty]
        private bool _isCharging;

        public BatteryViewModel(BatteryService batteryService)
        {
            _batteryService = batteryService;

            // 订阅底层服务的状态更新事件
            _batteryService.StatusUpdated += OnStatusUpdated;

            // 初始化默认状态
            SyncStatus();

            // 启动后台监控
            _batteryService.Start();
        }

        private void OnStatusUpdated(object? sender, EventArgs e)
        {
            // WPF 中跨线程更新 UI 绑定的属性，需调度到 UI 线程
            System.Windows.Application.Current.Dispatcher.Invoke(SyncStatus);
        }

        private void SyncStatus()
        {
            Percentage = _batteryService.Percentage;
            IsOnline = _batteryService.IsOnline;
            IsCharging = _batteryService.IsCharging;
        }
    }
}