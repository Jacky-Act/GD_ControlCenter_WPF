using CommunityToolkit.Mvvm.ComponentModel;
using System;

/*
 * 文件名: SpectrometerModel.cs
 * 模块: 核心领域模型 (Domain Models)
 * 描述: 本文件定义了光谱仪的配置实体与核心数据承载实体。
 * 架构规范: 
 * 1. 本文件内的类只允许包含数据和极其简单的状态逻辑，严禁包含任何业务计算或硬件操作。
 * 2. SpectralData 作为多线程传递的信使，在实例化后应被视为“只读”，禁止在 UI 线程二次修改其内部数组。
 */

namespace GD_ControlCenter_WPF.Models.Spectrometer
{
    #region 1. 硬件配置模型 (用于 UI 绑定与硬件初始化)

    /// <summary>
    /// 光谱仪配置模型。
    /// 职责：存储设备硬件信息与测量参数，支持 WPF 数据绑定以实现参数同步更新。
    /// </summary>
    public partial class SpectrometerConfig
    {
        // --- 身份标识 ---
        public string SerialNumber = string.Empty;
        public string DeviceName = string.Empty;

        // --- 运行时句柄与状态 ---
        /// <summary>
        /// SDK 操作句柄。初始化成功后由 AVS_Activate 返回，断开时销毁。
        /// </summary>
        public int DeviceHandle = -1;
        public bool IsConnected = false;

        // --- 采集参数配置 ---
        /// <summary>
        /// 积分时间（Integration Time），单位为毫秒 (ms)。
        /// </summary>
        public float IntegrationTimeMs = 100f;

        /// <summary>
        /// 硬件采样的平均次数（NrAverages）。
        /// </summary>
        public uint AveragingCount = 5;

        /// <summary>
        /// 起始像素索引。
        /// </summary>
        public ushort StartPixel = 0;

        /// <summary>
        /// 终止像素索引。
        /// </summary>
        public ushort StopPixel = 4095;

        // --- 高级特性 ---
        /// <summary>
        /// 是否开启硬件过曝（饱和）检测拦截。
        /// </summary>
        public bool IsSaturationDetectionEnabled = true;

        public float MinIntegrationTime = 5.0f; // 硬件允许的最小积分时间
    }

    #endregion

    #region 2. 核心数据载体 (用于跨线程/模块数据流转)

    /// <summary>
    /// 光谱数据实体 (DTO - Data Transfer Object)。
    /// 职责：承载一次完整测量所生成的波长与强度数组。
    /// 警告：作为跨线程传递的信使，一旦生成，严禁任何模块直接修改其内部的数组元素！
    /// </summary>
    public class SpectralData
    {
        /// <summary>
        /// 波长数组（X轴，单位：nm）。
        /// </summary>
        public double[] Wavelengths { get; set; } = Array.Empty<double>();

        /// <summary>
        /// 光强/计数数组（Y轴，单位：Counts）。
        /// </summary>
        public double[] Intensities { get; set; } = Array.Empty<double>();

        /// <summary>
        /// 光谱数据的精确生成时间戳。
        /// </summary>
        public DateTime AcquisitionTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 数据来源设备的序列号。
        /// 在多机拼接场景中，标明该数据是由哪个物理设备采集的（拼接后的数据通常标记为 "Combined_System"）。
        /// </summary>
        public string SourceDeviceSerial { get; set; } = string.Empty;

        public SpectralData() { }

        public SpectralData(double[] wavelengths, double[] intensities, string serial = "")
        {
            Wavelengths = wavelengths;
            Intensities = intensities;
            SourceDeviceSerial = serial;
        }
    }

    #endregion

    #region 3. 动态追踪模型 (用于时序图与 UI 交互)

    /// <summary>
    /// 动态特征峰追踪模型。
    /// 职责：记录用户在主界面标记的特征峰，并在后台实时计算中追踪该峰值的真实坐标。
    /// </summary>
    public partial class TrackedPeak : ObservableObject
    {
        /// <summary>
        /// 用户初始点击的基准波长。
        /// </summary>
        [ObservableProperty]
        private double _baseWavelength;

        /// <summary>
        /// 寻峰算法的搜索容差窗口 (±nm)。
        /// </summary>
        [ObservableProperty]
        private double _toleranceWindow = 0.2;

        /// <summary>
        /// 当前帧实际追踪到的真实最高点波长 (X)。
        /// </summary>
        [ObservableProperty]
        private double _currentWavelength;

        /// <summary>
        /// 当前帧实际追踪到的真实最高点光强 (Y)。
        /// </summary>
        [ObservableProperty]
        private double _currentIntensity;

        public TrackedPeak(double baseWavelength, double toleranceWindow = 2.0)
        {
            BaseWavelength = baseWavelength;
            ToleranceWindow = toleranceWindow;
            CurrentWavelength = baseWavelength;
            CurrentIntensity = 0;
        }
    }

    #endregion
}