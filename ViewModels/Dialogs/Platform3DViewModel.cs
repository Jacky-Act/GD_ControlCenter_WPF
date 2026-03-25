using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Models.Messages; 
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Platform3D;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System.Collections.ObjectModel;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class Platform3DViewModel : ObservableObject
    {
        private readonly IPlatform3DService _platformService;
        private readonly PlatformCalibrationService _calibrationService;
        private readonly JsonConfigService _jsonConfigService = null!;
        // 【新增】寻峰服务依赖
        private readonly PeakTrackingService _peakTrackingService;
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

        [ObservableProperty] private ObservableCollection<string> _peakWavelengths = new();
        [ObservableProperty] private string _selectedPeakWavelength = string.Empty;

        public Platform3DViewModel(
                IPlatform3DService platformService,
                PlatformCalibrationService calibrationService,
                JsonConfigService jsonConfigService,
                PeakTrackingService peakTrackingService,
                Action closeAction)
        {
            _platformService = platformService;
            _calibrationService = calibrationService;
            _jsonConfigService = jsonConfigService;
            _peakTrackingService = peakTrackingService;
            _closeAction = closeAction;

            // 初始化时加载主界面的寻峰数据
            LoadAvailablePeaks();

            var config = _jsonConfigService?.Load();
            StepDistance = config?.Platform3D?.DefaultStepDistance ?? 100.0;

            SyncPositionFromService();

            WeakReferenceMessenger.Default.Register<PlatformBoundaryMessage>(this, (r, m) =>
            {
                string boundaryName = m.IsMin ? "零点" : "最大值";

                // 【新增】：只要触碰物理边界（归零或撞最大值），立刻去底层拉取最新坐标刷新界面。
                // 使用 Dispatcher.Invoke 确保在 UI 主线程更新，防止后台线程更新 UI 报错。
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    SyncPositionFromService();
                });

                if (IsCalibrating)
                {
                    if (m.IsMin)
                    {
                        if (m.Axis == AxisType.X)
                            CalibrationStatus = $"⚠️ 已到达边界：X 轴触碰 {boundaryName} 限位，开始 Y 轴的移动。";
                        else if (m.Axis == AxisType.Y)
                            CalibrationStatus = $"⚠️ 已到达边界：Y 轴触碰 {boundaryName} 限位，开始 Z 轴的移动。";
                        else if (m.Axis == AxisType.Z)
                            CalibrationStatus = $"⚠️ 已到达边界：Z 轴触碰 {boundaryName} 限位，准备开始全轴寻优。";
                    }
                    else
                    {
                        CalibrationStatus = $"⚠️ 已到达边界：{m.Axis} 轴触碰 {boundaryName} 限位，开始折返。";
                    }
                }
                else
                {
                    CalibrationStatus = $"⚠️ 已到达边界：{m.Axis} 轴触碰 {boundaryName} 限位，运动中止。";
                }
            });
        }

        private void LoadAvailablePeaks()
        {
            PeakWavelengths.Clear();
            foreach (var peak in _peakTrackingService.TrackedPeaks)
            {
                // 格式化为 "XXX.XX nm"，与前端 UI 直接对应
                PeakWavelengths.Add($"{peak.BaseWavelength:F2} nm");
            }

            // 默认选中第一个，如果没有则为空
            SelectedPeakWavelength = PeakWavelengths.FirstOrDefault() ?? string.Empty;
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

            // 1. 拦截预检：按轴与方向精细化校验
            if (isPositive && _platformService.Status.IsAtMax[targetAxis])
            {
                CalibrationStatus = $"操作拒绝：{targetAxis} 轴已处于极限位置，无法继续向正方向移动。";
                return;
            }

            if (!isPositive)
            {
                if (targetAxis == AxisType.Z)
                {
                    // 【核心修改】：Z轴大于0时一律放行（靠硬件0点截停）；
                    // 只有当 Z轴 <= 0 时，才拦截超过 -2000 的移动请求。
                    if (_platformService.CurrentPosition.Z <= 0 &&
                        _platformService.CurrentPosition.Z - step < PlatformLimits.MinStepZ)
                    {
                        CalibrationStatus = $"操作拒绝：Z 轴将超过最低极限位置 ({PlatformLimits.MinStepZ})，无法继续向该方向移动。";
                        return;
                    }
                }
                else if (_platformService.Status.IsAtMin[targetAxis])
                {
                    // X、Y 轴保持常规零点拦截
                    CalibrationStatus = $"操作拒绝：{targetAxis} 轴已处于零点极限位置，无法继续向该方向移动。";
                    return;
                }
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
            // 防呆：如果平台正在移动，拒绝重复执行
            if (_platformService.Status.IsMoving) return;

            // 防呆：如果没有目标波长，直接拦截
            if (string.IsNullOrEmpty(SelectedPeakWavelength))
            {
                CalibrationStatus = "操作拒绝：未检测到目标波长，请先在主界面右键标记特征峰。";
                return;
            }

            IsCalibrating = true;

            try
            {
                using var cts = new CancellationTokenSource();

                // 提取目标波长数值
                double targetWl = double.TryParse(SelectedPeakWavelength.Split(' ')[0], out var val) ? val : 253.65;

                // === 步骤 1: 强制全轴机械归零 ===
                CalibrationStatus = "步骤 1/4: 正在执行全轴机械归零...";
                await _calibrationService.AutoHomeAllAxesAsync(cts.Token);

                SyncPositionFromService(); // 归零完成后，立刻把界面的 X, Y, Z 刷新为 0
                await Task.Delay(800, cts.Token); // 停顿 0.8 秒，让用户看清归零数值和准备下一个提示

                // === 步骤 2: X轴寻优 ===
                CalibrationStatus = $"步骤 2/4: 正在执行 X轴 中心点寻优 ({targetWl:F2} nm)...";
                await _calibrationService.StartAxisCalibrationAsync(AxisType.X, targetWl, 2.0, cts.Token);

                SyncPositionFromService(); // X轴倒车到最亮点后，立即把最新坐标刷新到界面
                await Task.Delay(800, cts.Token); // 停顿 0.8 秒，展示 X 轴成绩，提示即将开始 Y 轴

                // === 步骤 3: Y轴寻优 ===
                CalibrationStatus = $"步骤 3/4: 正在执行 Y轴 中心点寻优 ({targetWl:F2} nm)...";
                await _calibrationService.StartAxisCalibrationAsync(AxisType.Y, targetWl, 2.0, cts.Token);

                SyncPositionFromService(); // 刷新 Y 轴最新坐标
                await Task.Delay(800, cts.Token);

                // === 步骤 4: Z轴寻优 ===
                CalibrationStatus = $"步骤 4/4: 正在执行 Z轴 中心点寻优 ({targetWl:F2} nm)...";
                await _calibrationService.StartAxisCalibrationAsync(AxisType.Z, targetWl, 2.0, cts.Token);

                SyncPositionFromService(); // 刷新 Z 轴最新坐标

                // 【新增】给 Z 轴 1.5 秒的展示时间，让用户看清“开始折返”的提示以及最终更新的坐标！
                await Task.Delay(1500, cts.Token);

                // 全部执行完毕
                CalibrationStatus = "全自动空间校准已完成！";
            }
            catch (OperationCanceledException)
            {
                CalibrationStatus = "校准已手动终止";
            }
            catch (Exception ex)
            {
                CalibrationStatus = $"校准失败: {ex.Message}";
            }
            finally
            {
                // 兜底刷新，确保无论成功还是报错中断，UI 坐标都与底层物理位置保持绝对一致
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