using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    /// <summary>
    /// 硬件控制卡片基类
    /// </summary>
    public abstract partial class DeviceBaseViewModel : ObservableObject
    {
        // 设备名称（如：高压电源）
        [ObservableProperty]
        private string? _displayName;

        // 运行状态（绑定右侧 ToggleButton）
        [ObservableProperty]
        private bool _isRunning;

        // 实时显示值（如：10.5 V 或 50 RPM）
        [ObservableProperty]
        private string? _displayValue;

        // 主题色（用于区分不同设备的视觉风格）
        [ObservableProperty]
        private Brush? _themeColor;

        // 左侧区域点击命令（用于弹出详情配置窗口）
        public IRelayCommand OpenDetailCommand { get; }

        public IRelayCommand ToggleCommand { get; } // 新增切换命令

        protected DeviceBaseViewModel()
        {
            OpenDetailCommand = new RelayCommand(ExecuteOpenDetail);
            ToggleCommand = new RelayCommand(() => IsRunning = !IsRunning); // 点击取反
        }

        // 由子类实现具体的弹窗逻辑
        protected abstract void ExecuteOpenDetail();
    }
}