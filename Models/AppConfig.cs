/*
 * 文件名: AppConfig.cs
 * 描述: 本文件定义了软件的运行配置模型。
 * 用于持久化保存用户在界面上设置的各项参数（如电源电压、泵速等），
 * 确保软件重启后能恢复至上一次的运行参数状态。
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Models
{
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

        // 在 AppConfig 类中新增以下内容

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
        /// 记忆的时序图待测样品/通道名称列表。
        /// 仅保存名称，特征峰(X坐标)由每次运行时重新寻峰配置。
        /// </summary>
        public List<string> TimeSeriesSampleNames { get; set; } = new List<string>();

        // 可以在此继续添加其他设备的参数，例如：
        // public int LastPumpRpm { get; set; } = 100;
    }
}
