using GD_ControlCenter_WPF.Models.Spectrometer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    /// <summary>
    /// 单个光谱仪硬件操作接口
    /// 核心职责：封装与底层 SDK 的交互，提供异步测量、参数设置及状态管理
    /// </summary>
    public interface ISpectrometerService
    {
        // --- 属性状态 ---

        /// <summary>
        /// 当前光谱仪的配置信息（含句柄、序列号、积分时间等）
        /// </summary>
        SpectrometerConfig Config { get; }

        /// <summary>
        /// 标记当前是否正在进行持续测量
        /// </summary>
        bool IsMeasuring { get; }

        // --- 核心控制方法 ---

        /// <summary>
        /// 初始化光谱仪：激活设备并读取波长校准系数
        /// </summary>
        /// <returns>初始化是否成功</returns>
        Task<bool> InitializeAsync();

        /// <summary>
        /// 应用当前 Config 中的积分时间、平均次数等参数到硬件
        /// </summary>
        /// <returns>硬件返回的执行结果</returns>
        int ApplyConfiguration();

        /// <summary>
        /// 执行一次单次测量并返回数据
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>光谱数据实体</returns>
        Task<SpectralData> MeasureOnceAsync(CancellationToken ct = default);

        /// <summary>
        /// 安全地更新测量配置
        /// <param name="newIntegrationTime">积分时间</param>
        /// <param name="newAverages">平均次数</param>
        /// </summary>
        Task<int> UpdateConfigurationAsync(float newIntegrationTime, uint newAverages);

        /// <summary>
        /// 启动持续测量模式：数据将通过 Messenger 实时推送
        /// </summary>
        void StartContinuousMeasurement();

        /// <summary>
        /// 停止测量动作
        /// </summary>
        void StopMeasurement();

        // --- 资源管理 ---

        /// <summary>
        /// 断开连接并释放 SDK 资源
        /// </summary>
        void Dispose();
    }
}
