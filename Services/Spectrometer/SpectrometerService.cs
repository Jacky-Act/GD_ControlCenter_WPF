using C_Sharp_Application;
using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Models.Spectrometer;
using System.Runtime.InteropServices;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    public partial class SpectrometerService : ObservableObject, ISpectrometerService, IDisposable
    {
        // --- 静态字段与全局锁 ---

        /// <summary>
        /// 全局静态锁，确保多台设备不会同时向 SDK 下发控制指令。
        /// </summary>
        private static readonly object _globalSdkLock = new object();

        // --- 私有字段 ---

        /// <summary>
        /// 硬件操作异步锁，防止热更改测量参数时引发状态机冲突。
        /// </summary>
        private readonly SemaphoreSlim _hardwareLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 硬件测量完成回调委托实例，作为类字段持有以防止被 GC 回收。
        /// </summary>
        private readonly MeasurementCompletedCallback _callback;

        /// <summary>
        /// 将 C++ 异步回调转换为 C# 异步 Task 的关键容器。
        /// </summary>
        private TaskCompletionSource<SpectralData>? _measureTcs;

        /// <summary>
        /// 持续测量模式下的取消令牌源。
        /// </summary>
        private CancellationTokenSource? _continuousCts;

        /// <summary>
        /// 标识是否正在执行单次采样任务。
        /// </summary>
        private bool _isSingleMeasuring = false;

        /// <summary>
        /// 标识当前对象资源是否已被释放。
        /// </summary>
        private bool _isDisposed;

        // --- 事件 ---

        /// <summary>
        /// 当单次测量数据准备就绪并成功提取时触发。
        /// </summary>
        public event Action<SpectralData>? DataReady;

        // --- 属性 ---

        /// <summary>
        /// 获取当前光谱仪的硬件配置与参数模型。
        /// </summary>
        public SpectrometerConfig Config { get; }

        /// <summary>
        /// 获取当前设备是否正处于测量状态。
        /// </summary>
        public bool IsMeasuring { get; private set; }

        // --- 构造函数 ---

        /// <summary>
        /// 实例化光谱仪硬件服务。
        /// </summary>
        /// <param name="config">光谱仪的基础配置参数实体。</param>
        public SpectrometerService(SpectrometerConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _callback = OnMeasurementCompleted;
        }

        // --- 公开方法 ---

        /// <summary>
        /// 异步初始化光谱仪硬件：激活设备并读取波长校准系数与像素参数。
        /// </summary>
        /// <returns>返回初始化结果：true 为成功，false 为失败。</returns>
        public async Task<bool> InitializeAsync()
        {
            return await Task.Run(() =>
            {
                var identity = new AvantesSdk.AvsIdentityType
                {
                    m_SerialNumber = Config.SerialNumber,
                    m_UserFriendlyName = Config.DeviceName
                };

                int handle = AvantesSdk.AVS_Activate(ref identity);
                if (handle < AvantesSdk.ERR_SUCCESS) return false;

                Config.DeviceHandle = handle;
                Config.IsConnected = true;

                ushort pixels = 0;
                AvantesSdk.AVS_GetNumPixels(handle, ref pixels);
                Config.StopPixel = (ushort)(pixels - 1);

                return true;
            });
        }

        /// <summary>
        /// 异步执行一次单次测量任务，并等待数据返回。
        /// </summary>
        /// <param name="ct">异步操作的取消令牌（可选）。</param>
        /// <returns>返回采集到的光谱数据实体；若失败或被取消则返回 null。</returns>
        public async Task<SpectralData> MeasureOnceAsync(CancellationToken ct = default)
        {
            if (!Config.IsConnected || IsMeasuring || _isSingleMeasuring) return null!;

            _isSingleMeasuring = true;
            try
            {
                _measureTcs = new TaskCompletionSource<SpectralData>();
                ApplyConfiguration();

                IntPtr pCallback = Marshal.GetFunctionPointerForDelegate(_callback);
                int result = AvantesSdk.AVS_MeasureCallback(Config.DeviceHandle, pCallback, 1);

                if (result != AvantesSdk.ERR_SUCCESS) return null!;

                using (ct.Register(() => _measureTcs.TrySetCanceled()))
                {
                    return await _measureTcs.Task;
                }
            }
            finally
            {
                _isSingleMeasuring = false;
            }
        }

        /// <summary>
        /// 启动持续测量模式。设备将进入无限采集状态，数据通过 DataReady 事件推送。
        /// </summary>
        public void StartContinuousMeasurement()
        {
            if (!Config.IsConnected || IsMeasuring) return;

            IsMeasuring = true;
            _continuousCts = new CancellationTokenSource();

            lock (_globalSdkLock)
            {
                ApplyConfiguration();
                IntPtr pCallback = Marshal.GetFunctionPointerForDelegate(_callback);
                int result = AvantesSdk.AVS_MeasureCallback(Config.DeviceHandle, pCallback, -1);

                if (result != AvantesSdk.ERR_SUCCESS)
                {
                    IsMeasuring = false;
                    System.Diagnostics.Debug.WriteLine($"[异常] 设备启动失败！错误码: {result}");
                }
            }
        }

        /// <summary>
        /// 停止当前的持续测量动作，安全中断底层采集循环。
        /// </summary>
        public void StopMeasurement()
        {
            if (!Config.IsConnected || !IsMeasuring) return;

            IsMeasuring = false;

            if (_continuousCts != null)
            {
                _continuousCts.Cancel();
                _continuousCts.Dispose();
                _continuousCts = null;
            }

            lock (_globalSdkLock)
            {
                AvantesSdk.AVS_StopMeasure(Config.DeviceHandle);
            }
        }

        /// <summary>
        /// 异步安全地更新硬件测量配置。如在采集中，会自动暂停并恢复。
        /// </summary>
        /// <param name="newIntegrationTime">新的积分时间设定值（ms）。</param>
        /// <param name="newAverages">新的平均次数设定值。</param>
        /// <returns>返回底层硬件调用的结果状态码（0 表示成功）。</returns>
        public async Task<int> UpdateConfigurationAsync(float newIntegrationTime, uint newAverages)
        {
            if (_isSingleMeasuring) return -999;

            await _hardwareLock.WaitAsync();
            try
            {
                return await Task.Run(async () =>
                {
                    bool wasMeasuring = IsMeasuring;

                    if (wasMeasuring)
                    {
                        StopMeasurement();
                        await Task.Delay(100);
                    }

                    Config.IntegrationTimeMs = newIntegrationTime;
                    Config.AveragingCount = newAverages;

                    int result = ApplyConfiguration();

                    if (wasMeasuring && result == AvantesSdk.ERR_SUCCESS)
                    {
                        StartContinuousMeasurement();
                    }

                    return result;
                });
            }
            finally
            {
                _hardwareLock.Release();
            }
        }

        /// <summary>
        /// 将 Config 模型中的当前测量参数装载并下发到硬件。
        /// </summary>
        /// <returns>返回底层 SDK 的配置准备结果状态码。</returns>
        public int ApplyConfiguration()
        {
            var measConfig = new AvantesSdk.MeasConfigType
            {
                m_StartPixel = Config.StartPixel,
                m_StopPixel = Config.StopPixel,
                m_IntegrationTime = Config.IntegrationTimeMs,
                m_NrAverages = Config.AveragingCount,
                m_SaturationDetection = (byte)(Config.IsSaturationDetectionEnabled ? 1 : 0),
                m_Trigger = new AvantesSdk.TriggerType { m_Mode = 0, m_Source = 0, m_SourceType = 0 }
            };
            return AvantesSdk.AVS_PrepareMeasure(Config.DeviceHandle, ref measConfig);
        }

        /// <summary>
        /// 释放当前设备的硬件资源，停止采集并反激活底层句柄。
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            StopMeasurement();
            if (Config.DeviceHandle != -1)
            {
                AvantesSdk.AVS_Deactivate(Config.DeviceHandle);
            }
            DataReady = null;
            _isDisposed = true;
        }

        // --- 私有辅助方法 ---

        /// <summary>
        /// 响应底层 SDK 触发的硬件测量完成回调。
        /// </summary>
        /// <param name="handle">触发回调的光谱仪设备句柄。</param>
        /// <param name="status">底层回传的任务执行状态码。</param>
        private void OnMeasurementCompleted(ref int handle, ref int status)
        {
            if (!IsMeasuring) return;

            if (_measureTcs != null && !_measureTcs.Task.IsCompleted)
            {
                ProcessAndSetTcs(handle, status);
                return;
            }

            if (status == AvantesSdk.ERR_SUCCESS)
            {
                var data = FetchSpectralData(handle);
                if (data != null)
                {
                    DataReady?.Invoke(data);
                }
            }
        }

        /// <summary>
        /// 从指定的硬件句柄内存中抓取并提取有效范围内的光谱数据。
        /// </summary>
        /// <param name="handle">目标设备句柄。</param>
        /// <returns>返回清洗后的光谱数据实体，抓取失败时返回 null。</returns>
        private SpectralData? FetchSpectralData(int handle)
        {
            try
            {
                uint timestamp = 0;
                var wavelengths = new AvantesSdk.PixelArrayType();
                var intensities = new AvantesSdk.PixelArrayType();

                int resLambda = AvantesSdk.AVS_GetLambda(handle, ref wavelengths);
                int resData = AvantesSdk.AVS_GetScopeData(handle, ref timestamp, ref intensities);

                if (resLambda == AvantesSdk.ERR_SUCCESS && resData == AvantesSdk.ERR_SUCCESS)
                {
                    int validLength = Config.StopPixel + 1;

                    double[] validWavelengths = wavelengths.Value.Take(validLength).ToArray();
                    double[] validIntensities = intensities.Value.Take(validLength).ToArray();

                    return new SpectralData(validWavelengths, validIntensities, Config.SerialNumber);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"数据抓取异常: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 处理单次采样的底层返回结果，并将其投递给异步 Task。
        /// </summary>
        /// <param name="handle">目标设备句柄。</param>
        /// <param name="status">底层回传的任务执行状态码。</param>
        private void ProcessAndSetTcs(int handle, int status)
        {
            if (status != AvantesSdk.ERR_SUCCESS)
            {
                _measureTcs?.TrySetResult(null!);
                return;
            }

            var data = FetchSpectralData(handle);
            _measureTcs?.TrySetResult(data!);
        }
    }
}