using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    public class PlatformCalibrationService : IDisposable
    {
        private readonly IPlatform3DService _platformService;
        // 【修改2】注入全局的寻峰服务
        private readonly PeakTrackingService _peakTrackingService;

        // --- 寻优过程实时状态 ---
        private readonly Stopwatch _sweepStopwatch = new();
        private bool _isScanning = false;
        private double _maxIntensityFound = -1;
        private long _maxIntensityTimeMs = 0;

        // --- 中断控制 ---
        private CancellationTokenSource? _scanCts;
        private string _abortReason = string.Empty;

        // --- 寻优目标参数 ---
        private double _targetWl;

        // 构造函数增加 PeakTrackingService 依赖
        public PlatformCalibrationService(IPlatform3DService platformService, PeakTrackingService peakTrackingService)
        {
            _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
            _peakTrackingService = peakTrackingService ?? throw new ArgumentNullException(nameof(peakTrackingService));

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

                    // 统一给所有轴下发最大行程+500的退后指令，直到接收到限位信号强行中断
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

            var activeSpectrometer = SpectrometerManager.Instance.Devices.FirstOrDefault();

            // 【核心修改】：严格校验光谱仪对象是否存在，以及是否正在持续测量中
            if (activeSpectrometer == null || !activeSpectrometer.IsMeasuring)
            {
                throw new InvalidOperationException("操作拒绝：光谱仪未启动。请先在主界面点击“启动光谱”后再执行校准。");
            }

            _targetWl = targetWl;
            _maxIntensityFound = -1;
            _maxIntensityTimeMs = 0;
            _isScanning = true;
            _abortReason = string.Empty;

            long totalSweepTimeMs = 0;
            int maxDistance = GetMaxStep(axis);

            _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // 既然光谱仪已经在源源不断吐数据了，直接开始计时并移动即可
                _sweepStopwatch.Restart();

                await _platformService.MoveAxisAsync(axis, maxDistance + 500, true, _scanCts.Token);

                totalSweepTimeMs = _sweepStopwatch.ElapsedMilliseconds;
            }
            catch (OperationCanceledException)
            {
                if (!string.IsNullOrEmpty(_abortReason))
                    throw new InvalidOperationException(_abortReason);
                throw;
            }
            finally
            {
                _isScanning = false;
                _sweepStopwatch.Stop();

                // 【核心修改】：删除了 activeSpectrometer.StopMeasurement()

                _scanCts?.Dispose();
                _scanCts = null;
            }

            // --- 倒车逻辑 ---
            if (totalSweepTimeMs > 0)
            {
                if (_maxIntensityTimeMs == 0)
                {
                    throw new InvalidOperationException($"扫完全程未捕获到有效的光谱信号，{axis} 轴寻优已强行中止。");
                }

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

            // 【修改3】过曝熔断机制：直接终止移动并记录错误信息
            if (SpectrometerLogic.CheckSaturation(data))
            {
                _isScanning = false;
                _abortReason = "检测到光谱过曝（饱和）。校准已强行中止，请降低积分时间后重试。";
                _scanCts?.Cancel(); // 瞬间切断正在等待的 MoveAxisAsync 任务
                return;
            }

            // 【修改2】直接从寻峰服务中获取已经算好的实时光强（容差匹配在前面已经做过了）
            var matchedPeak = _peakTrackingService.TrackedPeaks
                .FirstOrDefault(p => Math.Abs(p.BaseWavelength - _targetWl) < 0.01);

            if (matchedPeak == null) return; // 当前波长没有被追踪则丢弃

            double currentIntensity = matchedPeak.CurrentIntensity;

            // 记录寻优过程中的最高点（因为过曝直接熔断了，所以这里无需处理复杂的饱和区间算法）
            if (currentIntensity > _maxIntensityFound)
            {
                _maxIntensityFound = currentIntensity;
                _maxIntensityTimeMs = _sweepStopwatch.ElapsedMilliseconds;
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
            _scanCts?.Dispose();
        }
    }
}