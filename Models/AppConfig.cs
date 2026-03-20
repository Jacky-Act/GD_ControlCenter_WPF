/*
 * 文件名: AppConfig.cs
 * 模块: 核心领域模型 (Domain Models)
 * 描述: 本文件定义了软件的全局运行配置模型。
 * 架构规范:
 * 1. 纯数据容器 (POCO)：用于序列化和反序列化 JSON 本地配置文件。
 * 2. 状态记忆：确保软件重启后，能精准恢复至上一次实验的运行参数状态。
 * 3. 默认值兜底：所有属性必须赋予合理的默认值，防止初次运行或配置文件损坏时引发空引用异常。
 */

using System;
using System.Collections.Generic;

namespace GD_ControlCenter_WPF.Models
{
    #region 1. 附属实体结构

    /// <summary>
    /// 时序图采样节点配置实体类。
    /// 职责：用于持久化保存时序图中通道的名称，以及与其绑定的特征峰(X坐标)。
    /// </summary>
    public class TimeSeriesSampleNode
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 用户在图表上选定的特征峰波长。
        /// 使用可空类型 (double?) 是为了兼容“已建通道但尚未绑定波长”的中间状态。
        /// </summary>
        public double? SelectedPeakX { get; set; }
    }


    /// <summary>
    /// 三维平台专属配置实体类
    /// </summary>
    public class Platform3DConfig
    {
        // --- 坐标记忆 ---
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int Z { get; set; } = 0;

        // --- 限位状态记忆 ---
        // 使用 string 作为键（"X", "Y", "Z"），以确保 JSON 序列化在各种环境下绝对安全
        public Dictionary<string, bool> IsAtMin { get; set; } = new()
        {
            { "X", false }, { "Y", false }, { "Z", false }
        };
        public Dictionary<string, bool> IsAtMax { get; set; } = new()
        {
            { "X", false }, { "Y", false }, { "Z", false }
        };

        // --- 用户偏好记忆 ---
        /// <summary>
        /// 默认手动移动步长 (mm)
        /// </summary>
        public double DefaultStepDistance { get; set; } = 100.0;
    }

    #endregion

    /// <summary>
    /// 系统全局配置实体类。
    /// </summary>
    public class AppConfig
    {
        #region 2. 基础硬件记忆参数 (高压 & 泵系)

        // ================== 高压电源 ==================

        /// <summary>
        /// 高压电源电压设定值 (V)。
        /// </summary>
        public int LastHvVoltage { get; set; } = 0;

        /// <summary>
        /// 高压电源电流设定值 (mA 或 uA)。
        /// </summary>
        public int LastHvCurrent { get; set; } = 0;

        // ================== 蠕动泵 ==================

        /// <summary>
        /// 蠕动泵转速 (RPM)。
        /// </summary>
        public int LastPumpSpeed { get; set; } = 0;

        /// <summary>
        /// 蠕动泵旋转方向。True 为顺时针（前向/输入），False 为逆时针（后向/输出）。
        /// </summary>
        public bool IsPumpClockwise { get; set; } = true;

        /// <summary>
        /// 蠕动泵流速方程斜率 K (y = Kx + B)。用于将转速转换为实际物理流速。
        /// </summary>
        public double PumpFlowRateK { get; set; } = 1.0;

        /// <summary>
        /// 蠕动泵流速方程截距 B。
        /// </summary>
        public double PumpFlowRateB { get; set; } = 0.0;

        // ================== 注射泵 ==================

        /// <summary>
        /// 注射泵的移动距离步数，范围 0-3000。
        /// </summary>
        public int LastSyringeDistance { get; set; } = 0;

        /// <summary>
        /// 注射泵运行方向。True 为输出（推），False 为输入（吸）。
        /// </summary>
        public bool IsSyringeOutput { get; set; } = true;

        #endregion

        #region 3. 光谱仪与采样控制参数

        // ================== 采样任务 ==================

        /// <summary>
        /// 自动化采样次数设定值。
        /// </summary>
        public int LastSampleCount { get; set; } = 1;

        /// <summary>
        /// 自动化采样间隔设定值（单位：秒），范围 0-3600。
        /// </summary>
        public int LastSampleInterval { get; set; } = 1;

        // ================== 光谱仪底层 ==================

        /// <summary>
        /// 全局硬件连接使能。若为 False，软件将不尝试连接和初始化光谱仪 SDK。
        /// </summary>
        public bool IsSpectrometerEnabled { get; set; } = true;

        /// <summary>
        /// 异常熄火自动重燃使能。检测到意外熄火时将自动触发点火序列。
        /// </summary>
        public bool IsAutoReigniteEnabled { get; set; } = false;

        /// <summary>
        /// 光谱仪积分时间（Integration Time, ms）。直接决定曝光量。
        /// </summary>
        public float LastIntegrationTime { get; set; } = 0f;

        /// <summary>
        /// 光谱仪硬件平均次数（Averaging Count）。用于底层降噪。
        /// </summary>
        public uint LastAveragingCount { get; set; } = 0;

        #endregion

        #region 4. 时序图追踪配置

        /// <summary>
        /// 记忆的时序图待测样品/通道节点列表。
        /// 职责：保存用户关注的特征峰坐标列表，使得软件下次打开时时序图的追踪线能直接恢复。
        /// </summary>
        public List<TimeSeriesSampleNode> TimeSeriesSampleNodes { get; set; } = new List<TimeSeriesSampleNode>();

        #endregion

        #region 5. 本地数据导出配置 (IO 路径与自动化)

        // ================== 串口记忆 ==================

        /// <summary>
        /// 上次成功连接的串口号 (如 "COM3")。启动时尝试优先重连。
        /// </summary>
        public string LastSerialPort { get; set; } = string.Empty;

        // ================== 导出路径 ==================

        /// <summary>
        /// 单次保存全谱图的默认导出路径。默认回退至系统桌面。
        /// </summary>
        public string SingleSaveExportPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        /// <summary>
        /// 时序图自动保存的默认导出目标文件夹。默认回退至系统桌面。
        /// </summary>
        public string TimeSeriesSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // ================== 自动化脚本 ==================

        /// <summary>
        /// 脚本保存的总采集帧数 (默认 100 帧)。
        /// </summary>
        public int ScriptSaveCount { get; set; } = 100;

        /// <summary>
        /// 脚本保存的物理时间间隔 (默认 5 秒)。
        /// </summary>
        public int ScriptSaveInterval { get; set; } = 5;

        /// <summary>
        /// 脚本自动落盘的目标文件夹。
        /// </summary>
        public string ScriptSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        #endregion

        #region 6. 三维平台配置记忆

        /// <summary>
        /// 三维平台参数配置集合
        /// </summary>
        public Platform3DConfig Platform3D { get; set; } = new Platform3DConfig();

        #endregion
    }
}
