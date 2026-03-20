using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq; // 确保引入 Linq 以使用 FirstOrDefault
using System.Threading;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    public class PlatformCalibrationService : IDisposable
    {
        private readonly IPlatform3DService _platformService;

        // 【移除】 private readonly ISpectrometerService _spectrometerService;

        // --- 寻优过程实时状态 ---
        private readonly Stopwatch _sweepStopwatch = new();
        private bool _isScanning = false;
        private double _maxIntensityFound = -1;
        private long _maxIntensityTimeMs = 0;

        // --- 饱和区间处理方案变量 ---
        private bool _isCurrentlySaturated = false;
        private long _saturationStartTime = -1;
        private const double SaturationThreshold = 65000.0;

        // --- 寻优目标参数 ---
        private double _targetWl;
        private double _tolerance;

        // 【修改】只保留 IPlatform3DService 注入
        public PlatformCalibrationService(IPlatform3DService platformService)
        {
            _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));

            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                OnSpectralDataReceived(m.Value);
            });
        }

        public async Task AutoHomeAllAxesAsync(CancellationToken parentCts = default)
        {
            var axesToHome = new List<AxisType> { AxisType.X, AxisType.Y, AxisType.Z };

            try
            {
                foreach (var axis in axesToHome)
                {
                    if (_platformService.Status.IsAtMin[axis]) continue;

                    using var axisTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentCts, axisTimeoutCts.Token);

                    int homeDistance = PlatformLimits.MaxValidStep + 500;
                    await _platformService.MoveAxisAsync(axis, homeDistance, false, linkedCts.Token);

                    await Task.Delay(500, parentCts);
                }
            }
            catch (OperationCanceledException)
            {
                _platformService.StopAll();
                throw;
            }
        }

        public async Task StartAxisCalibrationAsync(AxisType axis, double targetWl, double tolerance, CancellationToken ct = default)
        {
            if (!_platformService.Status.IsAtMin[axis])
                throw new InvalidOperationException($"{axis} 轴未归零，无法执行精度校准。");

            // 【新增】动态获取当前激活的光谱仪实例
            var activeSpectrometer = SpectrometerManager.Instance.Devices.FirstOrDefault();
            if (activeSpectrometer == null)
            {
                throw new InvalidOperationException("未检测到运行中的光谱仪，无法启动校准。请先在主界面启动光谱仪。");
            }

            _targetWl = targetWl;
            _tolerance = tolerance;
            _maxIntensityFound = -1;
            _maxIntensityTimeMs = 0;
            _isCurrentlySaturated = false;
            _isScanning = true;

            long totalSweepTimeMs = 0;
            int maxDistance = GetMaxStep(axis);

            try
            {
                // 【修改】使用动态获取的 activeSpectrometer 控制硬件
                activeSpectrometer.StartContinuousMeasurement();
                _sweepStopwatch.Restart();

                await _platformService.MoveAxisAsync(axis, maxDistance + 500, true, ct);

                totalSweepTimeMs = _sweepStopwatch.ElapsedMilliseconds;
            }
            finally
            {
                _isScanning = false;
                _sweepStopwatch.Stop();
                // 【修改】使用动态获取的 activeSpectrometer 停止测量
                activeSpectrometer.StopMeasurement();
            }

            if (totalSweepTimeMs > 0 && _maxIntensityTimeMs > 0)
            {
                double ratio = (double)_maxIntensityTimeMs / totalSweepTimeMs;
                ratio = Math.Clamp(ratio, 0.0, 1.0);

                int bestTargetStep = (int)Math.Round(maxDistance * ratio);
                int currentPos = _platformService.CurrentPosition[axis];
                int returnDistance = currentPos - bestTargetStep;

                if (returnDistance > 0)
                {
                    await Task.Delay(1000, ct);
                    await _platformService.MoveAxisAsync(axis, returnDistance, false, ct);
                }
            }
        }

        private void OnSpectralDataReceived(SpectralData data)
        {
            if (!_isScanning) return;

            double actualPeakWl = SpectrometerLogic.GetActualPeakWavelength(data, _targetWl, _tolerance);
            double currentIntensity = SpectrometerLogic.GetIntensityAtWavelength(data, actualPeakWl);

            bool isSaturated = currentIntensity >= SaturationThreshold;

            if (isSaturated)
            {
                if (!_isCurrentlySaturated)
                {
                    _saturationStartTime = _sweepStopwatch.ElapsedMilliseconds;
                    _isCurrentlySaturated = true;
                }

                _maxIntensityTimeMs = (_saturationStartTime + _sweepStopwatch.ElapsedMilliseconds) / 2;
            }
            else
            {
                _isCurrentlySaturated = false;
                if (currentIntensity > _maxIntensityFound)
                {
                    _maxIntensityFound = currentIntensity;
                    _maxIntensityTimeMs = _sweepStopwatch.ElapsedMilliseconds;
                }
            }
        }

        private int GetMaxStep(AxisType axis) => axis switch
        {
            AxisType.X => PlatformLimits.MaxStepX,
            AxisType.Y => PlatformLimits.MaxStepY,
            AxisType.Z => PlatformLimits.MaxStepZ,
            _ => 0
        };

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }
}