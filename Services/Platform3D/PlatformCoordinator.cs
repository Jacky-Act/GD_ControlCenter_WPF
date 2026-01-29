using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Platform3D.Logic;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Diagnostics;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    public class PlatformCoordinator : IDisposable
    {
        private readonly ISpectrometerService _spectrometerService;
        private readonly SpectrometerLogic _spectralLogic;
        private readonly CalibrationLogic _calibrationLogic;

        // 运行时状态
        private readonly Stopwatch _sweepStopwatch = new();
        private bool _isScanning = false;
        private double _maxIntensityFound = -1;
        private long _maxIntensityTimeMs = 0;

        // 配置参数
        private double _targetWl;
        private double _tolerance;
        private AxisType _currentAxis;

        // 饱和处理变量（方案 B 核心）
        private bool _isCurrentlySaturated = false;
        private long _saturationStartTime = -1;
        private const double SaturationThreshold = 65000.0; // 16位ADC阈值

        public PlatformCoordinator(ISpectrometerService spectrometerService, SpectrometerLogic spectralLogic, CalibrationLogic calibrationLogic)
        {
            _spectrometerService = spectrometerService;
            _spectralLogic = spectralLogic;
            _calibrationLogic = calibrationLogic;

            // 注册消息：每当光谱仪有一帧新数据（不管是单次还是持续模式）
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                OnSpectralDataReceived(m.Value);
            });
        }

        /// <summary>
        /// 核心联动方法：启动某轴的自动化寻优校准
        /// </summary>
        public async Task StartAxisCalibrationAsync(AxisType axis, double targetWl, double tolerance, CancellationToken ct = default)
        {
            _currentAxis = axis;
            _targetWl = targetWl;
            _tolerance = tolerance;

            // 1. 初始化监控状态
            _maxIntensityFound = -1;
            _maxIntensityTimeMs = 0;
            _isCurrentlySaturated = false;
            _isScanning = true;

            // 2. 开启光谱仪持续测量模式
            _spectrometerService.StartContinuousMeasurement();

            // 3. 启动计时器（与扫略动作同步）
            _sweepStopwatch.Restart();

            try
            {
                // 4. 调用 CalibrationLogic 的校准序列
                // 注意：CalibrateAxisAsync 内部会执行 MoveAxisAsync(toMax)，这需要一定时间
                // 我们等待这个 Task 完成。由于 OnSpectralDataReceived 在后台线程运行，
                // 它会在 calibrationTask 执行 MoveAxisAsync 的过程中不断更新最高点。
                await _calibrationLogic.CalibrateAxisAsync(axis, ct);
            }
            finally
            {
                // 停止监控与测量
                _isScanning = false;
                _sweepStopwatch.Stop();
                _spectrometerService.StopMeasurement();
            }
        }
        /// <summary>
        /// 实时处理逻辑（方案 B：几何中心法）
        /// </summary>
        private void OnSpectralDataReceived(SpectralData data)
        {
            if (!_isScanning) return;

            // 1. 每帧动态寻峰（应对漂移）
            double actualPeakWl = _spectralLogic.GetActualPeakWavelength(data, _targetWl, _tolerance);

            // 2. 读取该波长处的强度
            double currentIntensity = _spectralLogic.GetIntensityAtWavelength(data, actualPeakWl);

            // 3. 饱和逻辑判断
            bool isSaturated = currentIntensity >= SaturationThreshold;

            if (isSaturated)
            {
                if (!_isCurrentlySaturated)
                {
                    // 记录进入饱和的时间点
                    _saturationStartTime = _sweepStopwatch.ElapsedMilliseconds;
                    _isCurrentlySaturated = true;
                }
                // 计算当前饱和区间的几何中心 [T_start + (T_now - T_start)/2]
                _maxIntensityTimeMs = (_saturationStartTime + _sweepStopwatch.ElapsedMilliseconds) / 2;
            }
            else
            {
                _isCurrentlySaturated = false;
                // 正常的非饱和寻优
                if (currentIntensity > _maxIntensityFound)
                {
                    _maxIntensityFound = currentIntensity;
                    _maxIntensityTimeMs = _sweepStopwatch.ElapsedMilliseconds;
                }
            }

            // 4. 将计算出的最佳时间同步给校准逻辑
            _calibrationLogic.SetTargetByIntensityTime(
                _currentAxis,
                _sweepStopwatch.ElapsedMilliseconds,
                _maxIntensityTimeMs);
        }

        public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
