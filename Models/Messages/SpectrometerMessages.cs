using CommunityToolkit.Mvvm.Messaging.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;

/*
 * 文件名: SpectrometerMessages.cs
 * 模块: 消息总线契约 (Message Contracts)
 * 描述: 定义光谱仪相关的全局广播消息。通过弱引用消息解耦后台硬件采集与前端 UI 刷新。
 * 维护指南: 任何新增的消息都必须归类到对应的 #region 中，并写明“发送方”与“订阅方”。
 */

namespace GD_ControlCenter_WPF.Models.Messages
{
    #region 1. [数据流] 消息 (Data Flow)

    /// <summary>
    /// 核心光谱数据送达消息。
    /// 发送方: SpectrometerManager (单机直传或多机拼接完成后)
    /// 订阅方: 
    ///   - ControlPanelViewModel (用于刷新主界面波形图)
    ///   - TimeSeriesViewModel (用于提取特征峰数据并记录时序图)
    ///   - PeakTrackingService (用于计算特征峰漂移)
    /// </summary>
    public class SpectralDataMessage : ValueChangedMessage<SpectralData>
    {
        // 附带该帧数据产生时的硬件上下文状态
        public float IntegrationTime { get; set; }
        public uint AveragingCount { get; set; }

        public SpectralDataMessage(SpectralData value) : base(value) { }
    }

    #endregion

    #region 2. [状态流] 消息 (Status Flow)

    /// <summary>
    /// 光谱仪状态变更警告消息。
    /// 发送方: SpectrometerManager (检测到硬件断开、或像素饱和过曝时触发)
    /// 订阅方: MainViewModel 或 ControlPanelViewModel (用于在底部状态栏或界面上弹出警告)
    /// </summary>
    public class SpectrometerStatusMessage : ValueChangedMessage<SpectrometerConfig>
    {
        public SpectrometerStatusMessage(SpectrometerConfig value) : base(value) { }
    }

    #endregion

    #region 3. [视图控制流] 消息 (View Control Flow)

    // ================== 图表视图控制 ==================

    /// <summary>
    /// 请求光谱图自动缩放（自适应XY轴）消息。
    /// 发送方: ControlPanelViewModel (ViewModel 层)
    /// 订阅方: ControlPanelView.xaml.cs (View 层的 Code-Behind)
    /// </summary>
    public class AutoRangeRequestMessage { }

    /// <summary>
    /// 请求光谱图恢复到最大全局范围消息。
    /// 发送方: ControlPanelViewModel
    /// 订阅方: ControlPanelView.xaml.cs
    /// </summary>
    public class MaxRangeRequestMessage { }

    /// <summary>
    /// 请求时序图表切换布局（列表/矩阵）消息。
    /// 发送方: TimeSeriesViewModel
    /// 订阅方: TimeSeriesView.xaml.cs 
    /// </summary>
    public class ChangeTimeSeriesViewMessage
    {
        public bool IsMatrixView { get; }
        public ChangeTimeSeriesViewMessage(bool isMatrixView) => IsMatrixView = isMatrixView;
    }

    // ================== 参考图层控制 ==================

    /// <summary>
    /// 通知 View 渲染参考光谱图层 (红色虚线) 消息。
    /// 发送方: ControlPanelViewModel (读取历史 Excel 成功后)
    /// 订阅方: ControlPanelView.xaml.cs
    /// </summary>
    public class LoadReferencePlotMessage : ValueChangedMessage<SpectralData>
    {
        public LoadReferencePlotMessage(SpectralData value) : base(value) { }
    }

    /// <summary>
    /// 通知 View 清除屏幕上的参考光谱图层消息。
    /// 发送方: ControlPanelViewModel
    /// 订阅方: ControlPanelView.xaml.cs
    /// </summary>
    public class ClearReferencePlotMessage { }

    /// <summary>
    /// 脚本保存状态变更消息。
    /// 发送方: ControlPanelViewModel (保存任务结束或启动时)
    /// 订阅方: ScriptSaveViewModel (用于二级窗口实时同步按钮状态与锁定 UI)
    /// </summary>
    public class ScriptSaveStateChangedMessage : ValueChangedMessage<bool>
    {
        public ScriptSaveStateChangedMessage(bool isSaving) : base(isSaving) { }
    }

    #endregion
}