using C_Sharp_Application;
using CommunityToolkit.Mvvm.ComponentModel;
using GD_ControlCenter_WPF.Models.Spectrometer;
using System.Runtime.InteropServices;

/*
 * 文件名: SpectrometerService.cs
 * 模块: 硬件驱动层 (Hardware Abstraction Layer)
 * 描述: Avantes 光谱仪 SDK 的面向对象封装实现。
 * * 【多线程与并发架构说明 (必读)】
 * 1. 全局静态锁 (_globalSdkLock): 由于 Avantes SDK 的旧版 C++ 动态库并非完全线程安全，
 * 多台光谱仪同时调用 Start/Stop 指令时可能导致驱动崩溃，因此下发关键指令时必须全局排队。
 * 2. 实例异步锁 (_hardwareLock): 用于防止单个设备在“正在采集”时，被并发要求“修改积分时间”，
 * 保护该设备自身的状态机不出错。
 * 3. 委托生命周期 (_callback): 传递给 C++ 非托管内存的回调函数，必须作为 C# 类的全局变量持有，
 * 否则会被 .NET 的垃圾回收器 (GC) 意外清理，导致底层触发回调时发生 Access Violation (内存越界) 闪退。
 */

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    /// <summary>
    /// 光谱仪单机服务类。
    /// 负责管理单个物理光谱仪的生命周期、硬件参数配置以及异步数据采集。
    /// 继承自 ObservableObject，以便在有需要时将其部分状态暴露给 UI 层进行绑定。
    /// </summary>
    public partial class SpectrometerService : ObservableObject, ISpectrometerService, IDisposable
    {
        #region 1. 锁与底层资源管理

        /// <summary>
        /// 全局静态锁。
        /// 确保同一进程内，多台物理光谱仪不会在同一瞬间调用 AVS_MeasureCallback 或 AVS_StopMeasure，
        /// 从而避免底层 C++ SDK 发生 USB 总线资源竞争或崩溃。
        /// </summary>
        private static readonly object _globalSdkLock = new object();

        /// <summary>
        /// 实例级别的异步锁。
        /// 保护单台设备的硬件状态机（如：防止在执行 UpdateConfigurationAsync 时又被外部调用其他操作）。
        /// </summary>
        private readonly SemaphoreSlim _hardwareLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 【防坑标记】：硬件测量完成时的回调委托实例。
        /// 严禁将其改为方法内的局部变量！由于它会被传递到 C++ 非托管层，
        /// 必须作为类的全局引用持有，以防止 .NET GC 认为该对象无用而将其回收，从而导致严重的内存访问违例。
        /// </summary>
        private readonly MeasurementCompletedCallback _callback;

        /// <summary>
        /// 异步任务完成源。
        /// 核心作用：将基于事件的（Event-Based）底层 C++ 回调机制，优雅地封装为支持 await/async 的 C# 任务模型。
        /// </summary>
        private TaskCompletionSource<SpectralData>? _measureTcs;

        /// <summary>
        /// 持续测量模式下的取消令牌源。
        /// 用于在设备停止测量时，安全地中断任何正在排队的内部逻辑。
        /// </summary>
        private CancellationTokenSource? _continuousCts;

        /// <summary>
        /// 标识当前设备是否正在执行一次性的“单次测量”任务。
        /// 单次测量和持续测量是互斥的。
        /// </summary>
        private bool _isSingleMeasuring = false;

        /// <summary>
        /// 标识当前对象是否已被注销并释放资源，防止重复调用 Dispose 导致底层报错。
        /// </summary>
        private bool _isDisposed = false;

        #endregion

        #region 2. 接口属性与事件实现

        /// <summary>
        /// 核心事件：当底层硬件完成一次完整采集并成功解析为 C# 实体后触发。
        /// 注意：触发源在 C++ 的后台线程中，订阅者（如 UI）收到此事件后必须手动切回主线程。
        /// </summary>
        public event Action<SpectralData>? DataReady;

        /// <summary>
        /// 当前光谱仪的硬件配置与运行参数模型。
        /// </summary>
        public SpectrometerConfig Config { get; }

        /// <summary>
        /// 获取当前设备是否正处于无限循环的“持续测量”状态中。
        /// 仅可通过类内部方法（Start/Stop）安全地修改。
        /// </summary>
        public bool IsMeasuring { get; private set; }

        /// <summary>
        /// 实例化单个光谱仪硬件服务。
        /// </summary>
        /// <param name="config">光谱仪的基础配置参数实体，通常由 Manager 在发现设备时注入。</param>
        public SpectrometerService(SpectrometerConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));

            // 在构造时就锁定委托地址，确保整个生命周期内不会被 GC 回收
            _callback = OnMeasurementCompleted;
        }

        #endregion

        #region 3. 生命周期与配置管理

        /// <summary>
        /// 异步执行硬件初始化操作。
        /// 包含：激活硬件句柄、获取出厂像素点数量等关键基础信息。
        /// </summary>
        /// <returns>返回初始化是否成功。成功则为 true，且 Config 中的 DeviceHandle 会被赋值。</returns>
        public async Task<bool> InitializeAsync()
        {
            return await Task.Run(() =>
            {
                var identity = new AvantesSdk.AvsIdentityType
                {
                    m_SerialNumber = Config.SerialNumber,
                    m_UserFriendlyName = Config.DeviceName
                };

                // 1. 向底层发送激活指令，获取硬件句柄
                int handle = AvantesSdk.AVS_Activate(ref identity);
                if (handle < AvantesSdk.ERR_SUCCESS) return false;

                // 2. 【核心优化】：智能协商 ADC 分辨率模式
                // 优先尝试开启 16-bit 满血模式 (0-65535)
                int resHighRes = AvantesSdk.AVS_UseHighResAdc(handle, true);
                if (resHighRes == AvantesSdk.ERR_SUCCESS)
                {
                    System.Diagnostics.Debug.WriteLine($"[设备 {Config.SerialNumber}] 成功开启 16-bit 高分辨率模式。");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[设备 {Config.SerialNumber}] 开启 16-bit 失败，尝试显式降级到 14-bit...");

                    // 尝试显式锁定为 14-bit 兼容模式 (0-16383)
                    int resLowRes = AvantesSdk.AVS_UseHighResAdc(handle, false);

                    // 如果连 14-bit 都显式设置失败，说明通信异常或硬件极度老旧不支持此指令
                    if (resLowRes != AvantesSdk.ERR_SUCCESS)
                    {
                        System.Diagnostics.Debug.WriteLine($"[致命错误] 设备 {Config.SerialNumber} 切换 ADC 模式发生严重异常(错误码: {resLowRes})，初始化强制中止。");

                        // 初始化宣告失败，必须立刻把刚刚拿到的 handle 交还给操作系统，防止句柄泄露锁死端口！
                        AvantesSdk.AVS_Deactivate(handle);
                        return false;
                    }

                    System.Diagnostics.Debug.WriteLine($"[设备 {Config.SerialNumber}] 已成功回退并锁定为 14-bit 模式。");
                }

                // 3. 基础信息装载
                Config.DeviceHandle = handle;
                Config.IsConnected = true;

                // 动态获取当前设备支持的物理像素数
                ushort pixels = 0;
                AvantesSdk.AVS_GetNumPixels(handle, ref pixels);
                Config.StopPixel = (ushort)(pixels - 1);

                return true;
            });
        }

        /// <summary>
        /// 将当前 Config 对象中的测量参数（积分时间、平均次数等）转换为底层数据结构，并下发给硬件。
        /// 每次开始测量前，或修改参数后，都必须调用此方法。
        /// </summary>
        /// <returns>底层 SDK 返回的状态码。0 (ERR_SUCCESS) 表示准备成功。</returns>
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
        /// 异步安全地更新硬件测量配置。
        /// 核心机制：如果当前正在采集中，会自动暂停采集 -> 下发新参数 -> 恢复采集，确保硬件状态机不崩溃。
        /// </summary>
        /// <param name="newIntegrationTime">新的积分时间设定值（ms）。</param>
        /// <param name="newAverages">新的平均次数设定值。</param>
        /// <returns>返回底层硬件调用的结果状态码（0 表示成功，-999 表示正在执行不可打断的单次测量）。</returns>
        public async Task<int> UpdateConfigurationAsync(float newIntegrationTime, uint newAverages)
        {
            // 防呆校验：不允许在单次测量期间打断
            if (_isSingleMeasuring) return -999;

            // 申请该设备的独占操作权，防止 UI 狂点导致频繁启停崩溃
            await _hardwareLock.WaitAsync();
            try
            {
                return await Task.Run(async () =>
                {
                    bool wasMeasuring = IsMeasuring;

                    // 状态机保护：如果正在持续采集，先停下来
                    if (wasMeasuring)
                    {
                        StopMeasurement();
                        // 留出 100ms 物理缓冲时间，让底层 USB 句柄彻底释放上一轮的任务
                        await Task.Delay(100);
                    }

                    // 更新内存模型
                    Config.IntegrationTimeMs = newIntegrationTime;
                    Config.AveragingCount = newAverages;

                    // 真实下发到硬件
                    int result = ApplyConfiguration();

                    // 如果刚才是在采集状态，且参数更新成功，则自动恢复采集
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

        #endregion

        #region 4. 测量模式控制 (单次 / 持续)

        /// <summary>
        /// 异步执行单次测量，挂起当前线程直到底层硬件返回数据。
        /// </summary>
        /// <param name="ct">异步操作的取消令牌。如果在测量等待期间外部要求取消，可立即抛出异常并中断等待。</param>
        /// <returns>返回解析好的光谱数据实体；若失败或被取消则返回 null。</returns>
        public async Task<SpectralData> MeasureOnceAsync(CancellationToken ct = default)
        {
            // 校验前置条件：未连接、正在持续测量、或已经在执行单次测量时，直接拒绝
            if (!Config.IsConnected || IsMeasuring || _isSingleMeasuring) return null!;

            _isSingleMeasuring = true;
            try
            {
                // 初始化一个隧道，用于承接 C++ 回调抛回来的数据
                _measureTcs = new TaskCompletionSource<SpectralData>();

                // 确保参数是最新的
                ApplyConfiguration();

                // 获取托管委托在内存中的固定指针位置，传给 C++
                IntPtr pCallback = Marshal.GetFunctionPointerForDelegate(_callback);

                // 参数 1 表示向底层下发指令：只执行 1 次测量
                int result = AvantesSdk.AVS_MeasureCallback(Config.DeviceHandle, pCallback, 1);

                if (result != AvantesSdk.ERR_SUCCESS) return null!;

                // 绑定外部的取消令牌，如果外部（如 UI）要求紧急取消，则立即中断等待隧道
                using (ct.Register(() => _measureTcs.TrySetCanceled()))
                {
                    // 异步挂起，直到 OnMeasurementCompleted 回调中为 _measureTcs 赋值
                    return await _measureTcs.Task;
                }
            }
            finally
            {
                // 无论成功还是异常，一定要恢复状态位
                _isSingleMeasuring = false;
            }
        }

        /// <summary>
        /// 启动持续测量模式。设备将进入无限采集状态，数据通过 DataReady 事件源源不断地推送。
        /// </summary>
        public void StartContinuousMeasurement()
        {
            if (!Config.IsConnected || IsMeasuring) return;

            IsMeasuring = true;
            _continuousCts = new CancellationTokenSource();

            // 启动指令极易引发底层总线冲突，必须加全局静态锁排队
            lock (_globalSdkLock)
            {
                ApplyConfiguration();
                IntPtr pCallback = Marshal.GetFunctionPointerForDelegate(_callback);

                // 参数 -1 代表向底层下发指令：进入无限循环的持续采集模式
                int result = AvantesSdk.AVS_MeasureCallback(Config.DeviceHandle, pCallback, -1);

                // 如果启动失败，重置标志位并输出调试信息
                if (result != AvantesSdk.ERR_SUCCESS)
                {
                    IsMeasuring = false;
                    System.Diagnostics.Debug.WriteLine($"[异常] 设备启动失败！错误码: {result}");
                }
            }
        }

        /// <summary>
        /// 停止当前的持续测量动作，安全中断底层 C++ 采集循环。
        /// </summary>
        public void StopMeasurement()
        {
            if (!Config.IsConnected || !IsMeasuring) return;

            IsMeasuring = false;

            // 取消可能还在排队的后续处理令牌
            if (_continuousCts != null)
            {
                _continuousCts.Cancel();
                _continuousCts.Dispose();
                _continuousCts = null;
            }

            // 停止指令同样需加全局排队锁，防止与其它设备的启停指令发生冲突
            lock (_globalSdkLock)
            {
                AvantesSdk.AVS_StopMeasure(Config.DeviceHandle);
            }
        }

        #endregion

        #region 5. 底层 C++ 回调与数据解析 (Unmanaged Boundary)

        /// <summary>
        /// 核心回调函数：响应底层 SDK 触发的硬件测量完成事件。
        /// 【极度警告】：此方法运行在 C++ SDK 开辟的非托管后台线程上，严禁在此处直接操作 UI（会引发崩溃）！
        /// </summary>
        /// <param name="handle">触发回调的光谱仪设备句柄。</param>
        /// <param name="status">底层回传的任务执行状态码（0 表示成功）。</param>
        private void OnMeasurementCompleted(ref int handle, ref int status)
        {
            // 场景 A：如果当前处于单次测量模式，将提取的数据送入异步 Task 隧道
            if (_measureTcs != null && !_measureTcs.Task.IsCompleted)
            {
                ProcessAndSetTcs(handle, status);
                return;
            }

            // 防呆校验：如果已经被 C# 层标记为停止，直接丢弃迟来的底层回调数据
            if (!IsMeasuring) return;

            // 场景 B：持续测量模式下，状态正常则提取数据并通过事件广播给上层
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
        /// 内存读取函数：从指定的硬件句柄内存中，抓取并提取有效范围内的光谱数据。
        /// </summary>
        /// <param name="handle">目标设备句柄。</param>
        /// <returns>返回清洗并封装好的 C# 光谱数据实体，抓取失败或超时则返回 null。</returns>
        private SpectralData? FetchSpectralData(int handle)
        {
            try
            {
                uint timestamp = 0;

                // 声明承接非托管数组的托管结构体
                var wavelengths = new AvantesSdk.PixelArrayType();
                var intensities = new AvantesSdk.PixelArrayType();

                // 调用 SDK 方法，从硬件内存读取波长映射表和当前光强计数表
                int resLambda = AvantesSdk.AVS_GetLambda(handle, ref wavelengths);
                int resData = AvantesSdk.AVS_GetScopeData(handle, ref timestamp, ref intensities);

                // 双重校验：必须波长和光强都读取成功才认为是有效数据
                if (resLambda == AvantesSdk.ERR_SUCCESS && resData == AvantesSdk.ERR_SUCCESS)
                {
                    // 硬件通常会传回 4096 个双精度浮点数，但实际上我们要根据 StopPixel 裁剪出真实的有效段
                    int validLength = Config.StopPixel + 1;

                    double[] validWavelengths = wavelengths.Value.Take(validLength).ToArray();
                    double[] validIntensities = intensities.Value.Take(validLength).ToArray();

                    // 打包返回
                    return new SpectralData(validWavelengths, validIntensities, Config.SerialNumber);
                }
            }
            catch (Exception ex)
            {
                // 仅用于开发调试，线上环境静默处理即可
                System.Diagnostics.Debug.WriteLine($"数据抓取异常: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 辅助方法：处理单次采样的底层返回结果，并将其正确地投递给挂起的异步 Task。
        /// </summary>
        /// <param name="handle">目标设备句柄。</param>
        /// <param name="status">底层回传的任务执行状态码。</param>
        private void ProcessAndSetTcs(int handle, int status)
        {
            if (status != AvantesSdk.ERR_SUCCESS)
            {
                // 如果底层抛错，直接给外层返回 null，而不是一直卡死等待
                _measureTcs?.TrySetResult(null!);
                return;
            }

            var data = FetchSpectralData(handle);
            _measureTcs?.TrySetResult(data!);
        }

        #endregion

        #region 6. 资源释放 (Dispose)

        /// <summary>
        /// 释放当前设备的硬件资源。
        /// 停止采集，反激活底层 USB 句柄，注销事件。
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            // 1. 确保安全停机
            StopMeasurement();

            // 2. 将设备句柄交还给系统
            if (Config.DeviceHandle != -1)
            {
                AvantesSdk.AVS_Deactivate(Config.DeviceHandle);
            }

            // 3. 切断上层引用链
            DataReady = null;
            _isDisposed = true;
        }

        #endregion
    }
}