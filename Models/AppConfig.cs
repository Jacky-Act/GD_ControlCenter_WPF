/*
 * 文件名: AppConfig.cs
 * 描述: 定义软件全局运行配置模型，用于持久化存储硬件参数、实验设定及 UI 界面状态。
 * 维护指南: 合并了 [同事的硬件/路径/窗口记忆] 与 [你的测量模式切换] 逻辑。
 */

namespace GD_ControlCenter_WPF.Models
{
    #region 1. 附属实体结构

    /// <summary> 时序图采样通道配置实体 </summary>
    public class TimeSeriesSampleNode
    {
        public string Name { get; set; } = string.Empty;
        public double? SelectedPeakX { get; set; }
    }

    /// <summary> 三维平台运行配置实体 </summary>
    public class Platform3DConfig
    {
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int Z { get; set; } = 0;
        public Dictionary<string, bool> IsAtMin { get; set; } = new() { { "X", false }, { "Y", false }, { "Z", false } };
        public Dictionary<string, bool> IsAtMax { get; set; } = new() { { "X", false }, { "Y", false }, { "Z", false } };
        public double DefaultStepDistance { get; set; } = 100;
    }

    #endregion

    /// <summary>
    /// 系统全局配置根实体类。
    /// </summary>
    public class AppConfig
    {
        // ================== [你的业务核心] 测量模式定义 ==================
        public enum MeasurementMode { Continuous, FlowInjection }
        public MeasurementMode CurrentMeasurementMode { get; set; } = MeasurementMode.Continuous;


        #region 2. 电力与流体控制参数

        // ================== 高压电源记忆 ==================
        public int LastHvVoltage { get; set; } = 0;
        public int LastHvCurrent { get; set; } = 0;

        // ================== 蠕动泵控制记忆 ==================
        public int LastPumpSpeed { get; set; } = 0;
        public bool IsPumpClockwise { get; set; } = true;
        public double PumpFlowRateK { get; set; } = 1.0;
        public double PumpFlowRateB { get; set; } = 0.0;

        // ================== 注射泵控制记忆 ==================
        public int LastSyringeDistance { get; set; } = 0;
        public bool IsSyringeOutput { get; set; } = true;

        // ================== 点火系统配置 ==================
        public bool IsAutoReigniteEnabled { get; set; } = false;
        public double IgnitionDelaySeconds { get; set; } = 1.0;

        #endregion

        #region 3. 光谱采集与任务控制

        public int LastSampleCount { get; set; } = 1;
        public int LastSampleInterval { get; set; } = 1;
        public bool IsSpectrometerEnabled { get; set; } = true;
        public float LastIntegrationTime { get; set; } = 0f;
        public uint LastAveragingCount { get; set; } = 0;

        #endregion

        #region 4. 时序图追踪与存储配置

        public List<TimeSeriesSampleNode> TimeSeriesSampleNodes { get; set; } = new List<TimeSeriesSampleNode>();
        public string LastSerialPort { get; set; } = string.Empty;
        public string SingleSaveExportPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public string TimeSeriesSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public string ScriptSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public int ScriptSaveCount { get; set; } = 11;
        public int ScriptSaveInterval { get; set; } = 5;

        #endregion

        #region 5. 运动控制与 UI 窗口记忆 (这是同事的核心代码)

        public Platform3DConfig Platform3D { get; set; } = new Platform3DConfig();
        public double MainWindowWidth { get; set; } = 1280;
        public double MainWindowHeight { get; set; } = 800;
        public bool IsMainWindowMaximized { get; set; } = false;

        #endregion

        // --- 手动加上这一行，解决你的报错 ---
        public List<string> TimeSeriesSampleNames { get; set; } = new List<string>();
        // ----------------------------------
    }
}