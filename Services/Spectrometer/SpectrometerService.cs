using C_Sharp_Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using System.Runtime.InteropServices;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    public partial class SpectrometerService : ObservableObject, ISpectrometerService, IDisposable
    {
        private bool _isDisposed;
        private CancellationTokenSource? _continuousCts;    // 持续测量取消令牌
        private bool _isSingleMeasuring = false;    // 是否正在执行单次采样                                                    
        private readonly SemaphoreSlim _hardwareLock = new SemaphoreSlim(1, 1); // 防止热更改测量参数时，硬件卡死的锁
       
        private TaskCompletionSource<SpectralData>? _measureTcs;    // 将 C++ 回调转换为异步 Task 的关键容器        
        private readonly MeasurementCompletedCallback _callback;    // 定义回调委托，防止被 GC 回收

        public SpectrometerConfig Config { get; }
        public bool IsMeasuring { get; private set; }   

        public SpectrometerService(SpectrometerConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _callback = OnMeasurementCompleted; // 注册内部回调函数
        }

        /// <summary>
        /// 初始化光谱仪：激活设备并获取基本参数
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            return await Task.Run(() =>
            {
                var identity = new AvantesSdk.AvsIdentityType
                {
                    m_SerialNumber = Config.SerialNumber,
                    m_UserFriendlyName = Config.DeviceName
                };

                // 激活设备获取句柄
                int handle = AvantesSdk.AVS_Activate(ref identity);
                if (handle < AvantesSdk.ERR_SUCCESS) return false; // 负数表示错误

                Config.DeviceHandle = handle;
                Config.IsConnected = true;

                // 获取像素总数并更新配置
                ushort pixels = 0;
                AvantesSdk.AVS_GetNumPixels(handle, ref pixels);
                Config.StopPixel = (ushort)(pixels - 1);

                return true;
            });
        }

        /// <summary>
        /// 执行单次测量（非阻塞等待结果）
        /// </summary>
        public async Task<SpectralData> MeasureOnceAsync(CancellationToken ct = default)
        {
            if (!Config.IsConnected || IsMeasuring || _isSingleMeasuring) return null!;

            _isSingleMeasuring = true; // 标记单次采样开始
            try
            {
                _measureTcs = new TaskCompletionSource<SpectralData>();
                ApplyConfiguration();

                // 注册回调并启动测量
                IntPtr pCallback = Marshal.GetFunctionPointerForDelegate(_callback);
                int result = AvantesSdk.AVS_MeasureCallback(Config.DeviceHandle, pCallback, 1);

                if (result != AvantesSdk.ERR_SUCCESS) return null!;

                // 等待硬件返回或取消信号
                using (ct.Register(() => _measureTcs.TrySetCanceled()))
                {
                    return await _measureTcs.Task;
                }
            }
            finally
            {
                _isSingleMeasuring = false; // 无论成功失败，结束后释放标记
            }
        }

        /// <summary>
        /// 持续测量循环：利用 SDK 的 -1 (Infinite) 模式实现
        /// </summary>
        public void StartContinuousMeasurement()
        {
            if (!Config.IsConnected || IsMeasuring) return;

            IsMeasuring = true;
            _continuousCts = new CancellationTokenSource();

            ApplyConfiguration();              
            IntPtr pCallback = Marshal.GetFunctionPointerForDelegate(_callback);    // 获取回调函数指针            
            int result = AvantesSdk.AVS_MeasureCallback(Config.DeviceHandle, pCallback, -1);    // 传入 -1，指令硬件进入无限采集模式

            if (result != AvantesSdk.ERR_SUCCESS)
            {
                IsMeasuring = false;
                // 可以通过 Messenger 发送错误通知
            }
        }

        /// <summary>
        /// 停止测量
        /// </summary>
        public void StopMeasurement()
        {
            if (Config.IsConnected)
                AvantesSdk.AVS_StopMeasure(Config.DeviceHandle);

            IsMeasuring = false;

            if (_continuousCts != null)
            {
                _continuousCts.Cancel();  // 1. 发出通知
                _continuousCts.Dispose(); // 2. 释放内核句柄
                _continuousCts = null;    // 3. 彻底抹除引用
            }
        }

        /// <summary>
        /// 安全地更新测量配置
        /// 如果当前正在测量，该方法会自动暂停测量、应用新参数并恢复测量。
        /// </summary>
        /// <param name="newIntegrationTime">新的积分时间 (ms)</param>
        /// <param name="newAverages">新的平均次数</param>
        /// <returns>SDK 返回的结果码（0 表示成功）</returns>
        public async Task<int> UpdateConfigurationAsync(float newIntegrationTime, uint newAverages)
        {
            if (_isSingleMeasuring) return -999;    // 如果正在执行单次测量，返回一个自定义错误码或告知 UI 忙碌
           
            await _hardwareLock.WaitAsync();    // 引入信号量锁，防止按钮连点导致的逻辑混乱
            try
            {
                bool wasMeasuring = IsMeasuring;    // 记录当前测量状态

                if (wasMeasuring)
                {                    
                    StopMeasurement();  // 调用包含令牌取消的停止方法                   
                    await Task.Delay(100);  // 高频下给回调线程留出彻底清空缓存的时间
                }

                // 更新内存中的配置模型
                Config.IntegrationTimeMs = newIntegrationTime;
                Config.AveragingCount = newAverages;

                int result = ApplyConfiguration();  // 将配置下发给硬件

                // 如果之前处于测量状态且配置下发成功，则自动恢复
                if (wasMeasuring && result == AvantesSdk.ERR_SUCCESS)
                {
                    StartContinuousMeasurement();
                    // 可以通过 Messenger 发送错误通知
                }

                return result;
            }
            finally
            {
                _hardwareLock.Release();
            }
        }

        /// <summary>
        /// 将更新的参数配置到硬件
        /// </summary>
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
        /// 辅助方法：SDK 测量完成后的底层回调入口
        /// </summary>
        private void OnMeasurementCompleted(ref int handle, ref int status)
        {
            // 场景 A：如果当前有一个单次测量任务（MeasureOnceAsync）在挂起
            if (_measureTcs != null && !_measureTcs.Task.IsCompleted)
            {
                ProcessAndSetTcs(handle, status);
                return;
            }

            // 场景 B：如果当前处于持续测量模式（-1 模式）
            if (IsMeasuring && status == AvantesSdk.ERR_SUCCESS)
            {
                var data = FetchSpectralData(handle);
                if (data != null)
                {
                    // 通过信使机制广播给所有订阅者（UI、Logic等）
                    WeakReferenceMessenger.Default.Send(new SpectralDataMessage(data));
                }
            }
        }

        /// <summary>
        /// 辅助方法：从指定的硬件句柄中提取完整光谱数据
        /// </summary>
        /// <param name="handle">光谱仪设备句柄。</param>
        /// <returns>封装好的光谱数据实体，若获取失败则返回 null。</returns>
        private SpectralData? FetchSpectralData(int handle)
        {
            try
            {
                uint timestamp = 0;
                var wavelengths = new AvantesSdk.PixelArrayType();
                var intensities = new AvantesSdk.PixelArrayType();

                // 调用 SDK 获取原始数据
                int resLambda = AvantesSdk.AVS_GetLambda(handle, ref wavelengths);
                int resData = AvantesSdk.AVS_GetScopeData(handle, ref timestamp, ref intensities);

                if (resLambda == AvantesSdk.ERR_SUCCESS && resData == AvantesSdk.ERR_SUCCESS)
                {                   
                    int validLength = Config.StopPixel + 1; // 根据 stopPixel 索引计算有效长度

                    // 剔除末尾多余的 0
                    double[] validWavelengths = wavelengths.Value.Take(validLength).ToArray();
                    double[] validIntensities = intensities.Value.Take(validLength).ToArray();
                    
                    return new SpectralData(validWavelengths, validIntensities, Config.SerialNumber);   // 返回剔除无效 0 后的数据
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"数据抓取异常: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 辅助方法：处理单次测量的结果并通知异步等待者
        /// </summary>
        /// <param name="handle">硬件句柄。</param>
        /// <param name="status">硬件回传的状态码。</param>
        private void ProcessAndSetTcs(int handle, int status)
        {
            if (status != AvantesSdk.ERR_SUCCESS)
            {               
                _measureTcs?.TrySetResult(null!);   // 如果硬件返回错误，给等待者返回 null
                return;
            }
            
            var data = FetchSpectralData(handle);   // 调用抓取方法获取数据
            _measureTcs?.TrySetResult(data!);   // 给等待者返回数据
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            StopMeasurement();
            if (Config.DeviceHandle != -1)
            {
                AvantesSdk.AVS_Deactivate(Config.DeviceHandle); // 释放设备句柄
            }
            _isDisposed = true;
        }
    }
}
