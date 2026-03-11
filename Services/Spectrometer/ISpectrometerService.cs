using GD_ControlCenter_WPF.Models.Spectrometer;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    /// <summary>
    /// 单个光谱仪硬件操作接口
    /// 核心职责：定义与底层 SDK 交互的标准规范，提供异步测量、参数设置及状态管理。
    /// </summary>
    public interface ISpectrometerService : IDisposable
    {
        // --- 事件 ---

        /// <summary>
        /// 当单次测量数据准备就绪并成功提取时触发（替代全局消息广播）。
        /// </summary>
        event Action<SpectralData> DataReady;

        // --- 属性 ---

        /// <summary>
        /// 获取当前光谱仪的硬件配置与参数模型（含句柄、序列号、积分时间等）。
        /// </summary>
        SpectrometerConfig Config { get; }

        /// <summary>
        /// 获取当前设备是否正处于持续测量状态。
        /// </summary>
        bool IsMeasuring { get; }

        // --- 核心控制方法 ---

        /// <summary>
        /// 异步初始化光谱仪硬件：激活设备并读取波长校准系数与像素参数。
        /// </summary>
        /// <returns>返回初始化结果：true 为成功，false 为失败。</returns>
        Task<bool> InitializeAsync();

        /// <summary>
        /// 将当前 Config 中的积分时间、平均次数等参数装载并下发到硬件。
        /// </summary>
        /// <returns>返回底层 SDK 的配置准备结果状态码（0 表示成功）。</returns>
        int ApplyConfiguration();

        /// <summary>
        /// 异步执行一次单次测量任务，并等待数据返回。
        /// </summary>
        /// <param name="ct">异步操作的取消令牌（可选）。</param>
        /// <returns>返回采集到的光谱数据实体；若失败或被取消则返回 null。</returns>
        Task<SpectralData> MeasureOnceAsync(CancellationToken ct = default);

        /// <summary>
        /// 异步安全地更新硬件测量配置。如在采集中，会自动暂停并恢复。
        /// </summary>
        /// <param name="newIntegrationTime">新的积分时间设定值（ms）。</param>
        /// <param name="newAverages">新的平均次数设定值。</param>
        /// <returns>返回底层硬件调用的结果状态码（0 表示成功）。</returns>
        Task<int> UpdateConfigurationAsync(float newIntegrationTime, uint newAverages);

        /// <summary>
        /// 启动持续测量模式。设备将进入无限采集状态，数据通过 DataReady 事件推送。
        /// </summary>
        void StartContinuousMeasurement();

        /// <summary>
        /// 停止当前的持续测量动作，安全中断底层采集循环。
        /// </summary>
        void StopMeasurement();

        // --- 资源管理 ---

        /// <summary>
        /// 释放所有托管与非托管硬件资源，断开连接并反激活底层句柄。
        /// </summary>
        new void Dispose();
    }
}