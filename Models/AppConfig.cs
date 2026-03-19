/*
 * 文件名: AppConfig.cs
 * 描述: 本文件定义了软件的运行配置模型。
 * 用于持久化保存用户在界面上设置的各项参数（如电源电压、泵速等），
 * 确保软件重启后能恢复至上一次的运行参数状态。
 * 项目: GD_ControlCenter_WPF
 */

using System.Collections.Generic;

namespace GD_ControlCenter_WPF.Models
{
    /// <summary>
    /// 时序图采样节点配置实体类。
    /// 用于持久化保存通道名称以及与其绑定的特征峰(X坐标)。
    /// </summary>
    public class TimeSeriesSampleNode
    {
        public string Name { get; set; } = string.Empty;
        public double? SelectedPeakX { get; set; }
    }

    /// <summary>
    /// 系统全局配置实体类。
    /// 映射软件的本地配置文件，管理各硬件模块的记忆参数。
    /// </summary>
    public class AppConfig
    {
        // --- 高压电源设置参数 ---

        /// <summary>
        /// 高压电源电压设定值。
        /// </summary>
        public int LastHvVoltage { get; set; } = 0;

        /// <summary>
        /// 高压电源电流设定值。
        /// </summary>
        public int LastHvCurrent { get; set; } = 0;

        // --- 蠕动泵设置参数 ---

        /// <summary>
        /// 蠕动泵转速。
        /// </summary>
        public int LastPumpSpeed { get; set; } = 0;

        /// <summary>
        /// 蠕动泵旋转方向。True 为顺时针（前向），False 为逆时针（后向）。
        /// </summary>
        public bool IsPumpClockwise { get; set; } = true;

        // 【新增】流速标定参数
        public double PumpFlowRateK { get; set; } = 1.0; // 流速方程斜率 K
        public double PumpFlowRateB { get; set; } = 0.0; // 流速方程截距 B

        // --- 注射泵设置参数 ---

        /// <summary>
        /// 注射泵的移动距离步数，范围 0-3000。
        /// </summary>
        public int LastSyringeDistance { get; set; } = 0;

        /// <summary>
        /// 注射泵运行方向。True 为输出（推），False 为输入（吸）。
        /// </summary>
        public bool IsSyringeOutput { get; set; } = true;

        // --- 采样配置设置参数 ---

        /// <summary>
        /// 采样次数设定值。
        /// </summary>
        public int LastSampleCount { get; set; } = 1;

        /// <summary>
        /// 采样间隔设定值（单位：秒），范围 0-3600
        /// </summary>
        public int LastSampleInterval { get; set; } = 1;

        // --- 串口通信设置参数 ---

        /// <summary>
        /// 上次成功连接的串口号。
        /// </summary>
        public string LastSerialPort { get; set; } = string.Empty;

        // --- 光谱仪设置参数 ---

        /// <summary>
        /// 光谱仪积分时间（ms）。
        /// </summary>
        public float LastIntegrationTime { get; set; } = 0f;

        /// <summary>
        /// 光谱仪平均次数。
        /// </summary>
        public uint LastAveragingCount { get; set; } = 0;

        public bool IsSpectrometerEnabled { get; set; } = true;
        public bool IsAutoReigniteEnabled { get; set; } = false;

        // --- 时序图采样配置参数 ---

        /// <summary>
        /// 记忆的时序图待测样品/通道节点列表。
        /// 保存通道名称以及对应的特征峰坐标，生命周期与主界面寻峰线保持同步。
        /// </summary>
        public List<TimeSeriesSampleNode> TimeSeriesSampleNodes { get; set; } = new List<TimeSeriesSampleNode>();

        // --- 数据导出配置参数 ---

        /// <summary>
        /// 单次保存全谱图的默认导出路径。
        /// </summary>
        public string SingleSaveExportPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        /// <summary>
        /// 时序图自动保存的默认导出路径
        /// </summary>
        public string TimeSeriesSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // --- 脚本保存配置参数 ---

        /// <summary>
        /// 脚本保存的总次数 (默认 100 次)
        /// </summary>
        public int ScriptSaveCount { get; set; } = 100;

        /// <summary>
        /// 脚本保存的时间间隔 (默认 5 秒)
        /// </summary>
        public int ScriptSaveInterval { get; set; } = 5;

        /// <summary>
        /// 脚本保存的目标文件夹
        /// </summary>
        public string ScriptSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }
}