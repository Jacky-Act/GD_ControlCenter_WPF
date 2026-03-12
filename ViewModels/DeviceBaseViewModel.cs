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

        // 动态控制按钮上的文字
        [ObservableProperty]
        private string _buttonText = "启动";

        // 新增：忙碌状态锁。改变时会通知 ToggleCommand 刷新可用性
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
        private bool _isBusy;

        // 左侧区域点击命令（用于弹出详情配置窗口）
        public IRelayCommand OpenDetailCommand { get; }

        // 右侧启停命令
        public IRelayCommand ToggleCommand { get; } 

        protected DeviceBaseViewModel()
        {
            OpenDetailCommand = new RelayCommand(ExecuteOpenDetail);
            //ToggleCommand = new RelayCommand(() => IsRunning = !IsRunning); // 点击取反
            // 核心修改：当 IsBusy 为 true 时，命令返回 false，WPF 会自动把绑定的按钮置灰（不可点击）
            ToggleCommand = new RelayCommand(() => IsRunning = !IsRunning, () => !IsBusy);
        }

        /// <summary>
        /// 拦截 IsRunning 属性的变更，提供默认的 启动/停止 文字切换
        /// </summary>
        /// <param name="value">最新的 IsRunning 状态</param>
        partial void OnIsRunningChanged(bool value)
        {
            // 如果子类设备（如注射泵）没有正在执行特殊的自动化序列（未被锁定）
            // 则执行标准的文字切换，确保高压电源、蠕动泵等设备的按钮文字正常改变
            if (!IsBusy)
            {
                ButtonText = value ? "停止" : "启动";
            }
        }

        // 由子类实现具体的弹窗逻辑
        protected abstract void ExecuteOpenDetail();
    }
}