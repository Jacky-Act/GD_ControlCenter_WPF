using GD_ControlCenter_WPF.Models.Spectrometer;

/*
 * 文件名: ISpectrometerService.cs
 * 模块: 硬件驱动层 (Hardware Abstraction Layer)
 * 描述: 单个光谱仪硬件操作的标准契约。
 * 架构规范:
 * 1. 业务层（如 Manager 或 ViewModel）只允许依赖此接口，绝对不能直接调用底层 SDK。
 * 2. 隐藏底层 C++ 句柄的概念，对外只暴露面向对象的 Config 和 SpectralData。
 */

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    public interface ISpectrometerService : IDisposable
    {
        #region 1. 状态与配置 (State & Configuration)

        /// <summary>
        /// 获取当前光谱仪的硬件配置与参数模型。
        /// </summary>
        SpectrometerConfig Config { get; }

        /// <summary>
        /// 获取当前设备是否正处于“持续测量”状态。
        /// </summary>
        bool IsMeasuring { get; }

        #endregion

        #region 2. 数据流事件 (Data Event)

        /// <summary>
        /// 当底层硬件完成一次完整的数据采集并成功解析后触发。
        /// 注意：此事件可能在后台线程触发，订阅方若要更新 UI 必须自行 Dispatcher.Invoke。
        /// </summary>
        event Action<SpectralData> DataReady;

        #endregion

        #region 3. 核心控制动作 (Core Controls)

        /// <summary>
        /// 异步初始化光谱仪硬件：激活设备并读取硬件出厂参数（如最大像素数）。
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// 将当前 Config 模型中的参数（如积分时间）强行装载下发到硬件。
        /// 通常在开始测量前调用。
        /// </summary>
        int ApplyConfiguration();

        /// <summary>
        /// 异步安全地更新硬件测量配置。
        /// 内部机制：如果当前正在采集中，会自动暂停采集 -> 下发新参数 -> 恢复采集，确保硬件状态机不崩溃。
        /// </summary>
        Task<int> UpdateConfigurationAsync(float newIntegrationTime, uint newAverages);

        #endregion

        #region 4. 测量模式 (Measurement Modes)

        /// <summary>
        /// 异步执行单次测量，挂起当前线程直到底层硬件返回数据。
        /// </summary>
        Task<SpectralData> MeasureOnceAsync(CancellationToken ct = default);

        /// <summary>
        /// 启动持续测量模式。设备将进入无限采集循环，数据通过 DataReady 事件源源不断地推送。
        /// </summary>
        void StartContinuousMeasurement();

        /// <summary>
        /// 停止当前的持续测量动作，安全中断底层 C++ 采集循环。
        /// </summary>
        void StopMeasurement();

        #endregion
    }
}