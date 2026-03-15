using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs // 必须与文件夹路径和调用处完全一致
{
    public partial class Platform3DViewModel : ObservableObject
    {
        // 当前坐标及限位 (示例数据)
        [ObservableProperty] private double _currentX = 0;
        [ObservableProperty] private double _currentY = 0;
        [ObservableProperty] private double _currentZ = 0;
        [ObservableProperty] private double _maxX = 200;
        [ObservableProperty] private double _maxY = 200;
        [ObservableProperty] private double _maxZ = 100;

        // 手动控制参数
        [ObservableProperty] private int _selectedAxisIndex = 0;      // 0:X, 1:Y, 2:Z
        [ObservableProperty] private int _selectedDirectionIndex = 0; // 0:正, 1:反
        [ObservableProperty] private double _stepDistance = 1.0;

        // 自动校准
        [ObservableProperty] private string _calibrationStatus = "待命：请选择模式后点击开始";
        [ObservableProperty] private bool _isCalibrating;
        [ObservableProperty] private ObservableCollection<string> _calibrationModes = new() { "全轴复位校准", "中心点寻优", "焦距补偿" };
        [ObservableProperty] private string _selectedCalibrationMode = string.Empty;

        private readonly Action _closeAction;

        // 更新构造函数以接收关闭回调
        public Platform3DViewModel(Action closeAction)
        {
            _closeAction = closeAction;

            // 如果需要初始化数据，可以在这里调用
            SelectedCalibrationMode = CalibrationModes.FirstOrDefault() ?? "";
        }

        [RelayCommand]
        private async Task MoveAsync()
        {
            string axis = SelectedAxisIndex switch { 0 => "X", 1 => "Y", _ => "Z" };
            double direction = SelectedDirectionIndex == 0 ? 1.0 : -1.0;
            double targetMove = StepDistance * direction;

            await Task.CompletedTask;

            CalibrationStatus = $"正在移动 {axis} 轴: {targetMove} mm";
        }

        [RelayCommand]
        private async Task StartCalibrationAsync()
        {
            if (string.IsNullOrEmpty(SelectedCalibrationMode)) return;

            IsCalibrating = true;
            CalibrationStatus = $"执行中: {SelectedCalibrationMode}...";

            // 模拟校准过程
            await Task.Delay(2000);

            IsCalibrating = false;
            CalibrationStatus = "校准成功完成";
        }
    }
}