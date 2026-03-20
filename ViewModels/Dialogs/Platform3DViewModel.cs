using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Models.Messages; 
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Platform3D;
using System.Collections.ObjectModel;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class Platform3DViewModel : ObservableObject
    {
        private readonly IPlatform3DService _platformService;
        private readonly PlatformCalibrationService _calibrationService;
        private readonly JsonConfigService _jsonConfigService = null!;
        private readonly Action _closeAction;

        [ObservableProperty] private double _currentX;
        [ObservableProperty] private double _currentY;
        [ObservableProperty] private double _currentZ;

        [ObservableProperty] private double _maxX = PlatformLimits.MaxStepX;
        [ObservableProperty] private double _maxY = PlatformLimits.MaxStepY;
        [ObservableProperty] private double _maxZ = PlatformLimits.MaxStepZ;

        [ObservableProperty] private int _selectedAxisIndex = 0;
        [ObservableProperty] private int _selectedDirectionIndex = 0;
        [ObservableProperty] private double _stepDistance = 100.0;

        // 桥接属性保持不变
        public bool IsXSelected { get => SelectedAxisIndex == 0; set { if (value) { SelectedAxisIndex = 0; OnPropertyChanged(nameof(IsXSelected)); } } }
        public bool IsYSelected { get => SelectedAxisIndex == 1; set { if (value) { SelectedAxisIndex = 1; OnPropertyChanged(nameof(IsYSelected)); } } }
        public bool IsZSelected { get => SelectedAxisIndex == 2; set { if (value) { SelectedAxisIndex = 2; OnPropertyChanged(nameof(IsZSelected)); } } }
        public bool IsDirPositive { get => SelectedDirectionIndex == 0; set { if (value) { SelectedDirectionIndex = 0; OnPropertyChanged(nameof(IsDirPositive)); } } }
        public bool IsDirNegative { get => SelectedDirectionIndex == 1; set { if (value) { SelectedDirectionIndex = 1; OnPropertyChanged(nameof(IsDirNegative)); } } }

        [ObservableProperty] private string _calibrationStatus = "待命：系统已就绪";
        [ObservableProperty] private bool _isCalibrating;
        [ObservableProperty] private ObservableCollection<string> _calibrationModes = new() { "全轴复位校准", "单轴中心点寻优" };
        [ObservableProperty] private string _selectedCalibrationMode = string.Empty;

        [ObservableProperty] private ObservableCollection<string> _peakWavelengths = new() { "253.65 (Hg)", "589.00 (Na)" };
        [ObservableProperty] private string _selectedPeakWavelength = string.Empty;

        public Platform3DViewModel(IPlatform3DService platformService, PlatformCalibrationService calibrationService, JsonConfigService jsonConfigService, Action closeAction)
        {
            _platformService = platformService;
            _calibrationService = calibrationService;
            _jsonConfigService = jsonConfigService;
            _closeAction = closeAction;

            SelectedCalibrationMode = CalibrationModes.FirstOrDefault() ?? "";
            SelectedPeakWavelength = PeakWavelengths.FirstOrDefault() ?? "";

            var config = _jsonConfigService?.Load();
            StepDistance = config?.Platform3D?.DefaultStepDistance ?? 100.0;

            SyncPositionFromService();

            // 订阅物理限位消息
            WeakReferenceMessenger.Default.Register<PlatformBoundaryMessage>(this, (r, m) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 接收信号瞬间立刻同步坐标
                    SyncPositionFromService();

                    string boundaryName = m.IsMin ? "零点 (0)" : "最大极值";
                    // 专属的到达边界提示语
                    CalibrationStatus = $"⚠️ 已到达边界：{m.Axis} 轴触碰 {boundaryName} 限位，运动中止。";
                });
            });
        }

        private void SyncPositionFromService()
        {
            CurrentX = _platformService.CurrentPosition.X;
            CurrentY = _platformService.CurrentPosition.Y;
            CurrentZ = _platformService.CurrentPosition.Z;
        }

        [RelayCommand]
        private async Task MoveAsync()
        {
            if (_platformService.Status.IsMoving || IsCalibrating) return;

            AxisType targetAxis = SelectedAxisIndex switch { 0 => AxisType.X, 1 => AxisType.Y, _ => AxisType.Z };
            bool isPositive = SelectedDirectionIndex == 0;
            int step = (int)Math.Round(StepDistance);

            // 1. 拦截预检：如果是起步前就发现超限，使用“无法移动”文案
            if ((isPositive && _platformService.Status.IsAtMax[targetAxis]) ||
                (!isPositive && _platformService.Status.IsAtMin[targetAxis]))
            {
                CalibrationStatus = $"🚫 操作拒绝：{targetAxis} 轴已处于极限位置，无法继续向该方向移动。";
                return;
            }

            CalibrationStatus = $"正在手动移动 {targetAxis} 轴...";

            // 2. 正常下发移动
            bool success = await _platformService.MoveAxisAsync(targetAxis, step, isPositive);

            // 3. 【核心修复】无论成功还是因限位被打断，必定刷新一次坐标！
            SyncPositionFromService();

            if (success)
            {
                CalibrationStatus = "移动完成。";
            }
            // 💡 注意：如果 success 为 false（中途被打断），此处不再写 else 分支。
            // 因为底层的 PlatformBoundaryMessage 已经把更精准的“已到达边界”写进了 CalibrationStatus，
            // 此时盲目 overwrite 会导致信息丢失。
        }

        [RelayCommand]
        private async Task StartCalibrationAsync()
        {
            if (string.IsNullOrEmpty(SelectedCalibrationMode) || _platformService.Status.IsMoving) return;

            IsCalibrating = true;
            CalibrationStatus = $"执行中: {SelectedCalibrationMode}...";

            try
            {
                using var cts = new CancellationTokenSource();
                if (SelectedCalibrationMode == "全轴复位校准")
                    await _calibrationService.AutoHomeAllAxesAsync(cts.Token);
                else if (SelectedCalibrationMode == "单轴中心点寻优")
                {
                    AxisType targetAxis = SelectedAxisIndex switch { 0 => AxisType.X, 1 => AxisType.Y, _ => AxisType.Z };
                    double targetWl = double.TryParse(SelectedPeakWavelength.Split(' ')[0], out var val) ? val : 253.65;
                    await _calibrationService.StartAxisCalibrationAsync(targetAxis, targetWl, 2.0, cts.Token);
                }

                CalibrationStatus = "校准成功完成";
            }
            catch (OperationCanceledException) { CalibrationStatus = "校准已终止"; }
            catch (Exception ex) { CalibrationStatus = $"校准失败: {ex.Message}"; }
            finally
            {
                SyncPositionFromService();
                IsCalibrating = false;
            }
        }

        public void SavePreferences()
        {
            var config = _jsonConfigService.Load();

            if (config.Platform3D == null)
                config.Platform3D = new Platform3DConfig();

            config.Platform3D.DefaultStepDistance = StepDistance;

            // 【修复第154行参数缺失】：将 config 传入 Save 方法
            _jsonConfigService.Save(config);
        }
    }
}