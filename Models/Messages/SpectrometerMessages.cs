using CommunityToolkit.Mvvm.Messaging.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;

/*
 * 文件名: SpectrometerMessages.cs
 * 描述: 定义光谱仪相关的状态、数据分发及视图控制消息。
 * 涵盖了单台光谱仪采样、多台光谱拼接、设备硬件状态变更以及UI视图操作的通知逻辑。
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Models.Messages
{
    /// <summary>
    /// 单个光谱仪数据消息。
    /// 负责传输单次采样或持续测量的光谱实体。
    /// 典型订阅者：实时波形图展示、光谱处理逻辑类、实验数据持久化记录模块。
    /// </summary>
    public class SpectralDataMessage : ValueChangedMessage<SpectralData>
    {
        public SpectralDataMessage(SpectralData value) : base(value)
        {
        }
    }

    /// <summary>
    /// 光谱仪状态变更消息。
    /// 当光谱仪连接成功、意外断开或检测到像素饱和报警时发布。
    /// 典型订阅者：状态栏 UI。
    /// </summary>
    public class SpectrometerStatusMessage : ValueChangedMessage<SpectrometerConfig>
    {
        public SpectrometerStatusMessage(SpectrometerConfig value) : base(value)
        {
        }
    }

    /// <summary>
    /// 请求光谱图自动调整范围的消息。
    /// 典型订阅者：包含光谱图控件的视图后台 (如 ControlPanelView.xaml.cs)。
    /// </summary>
    public class AutoRangeRequestMessage { }

    /// <summary>
    /// 请求光谱图恢复到最大全局范围的消息
    /// </summary>
    public class MaxRangeRequestMessage { }

    /// <summary>
    /// 请求时序图表切换布局（列表/矩阵）的消息
    /// </summary>
    public class ChangeTimeSeriesViewMessage
    {
        public bool IsMatrixView { get; }

        public ChangeTimeSeriesViewMessage(bool isMatrixView)
        {
            IsMatrixView = isMatrixView;
        }
    }

    /// <summary>
    // 用于通知 View 切换光谱视图模式
    /// </summary>
    public class ChangeSpectrumViewMessage
    {
        public string Mode { get; } // "TOTAL" 或 "ELEMENT"
        public ChangeSpectrumViewMessage(string mode)
        {
            Mode = mode;
        }
    }
}
