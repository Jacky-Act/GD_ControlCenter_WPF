using CommunityToolkit.Mvvm.Messaging.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;

/*
 * 文件名: SpectrometerMessages.cs
 * 描述: 定义光谱仪相关的状态及数据分发消息。
 * 涵盖了单台光谱仪采样、多台光谱拼接以及设备硬件状态变更的通知逻辑。
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Models.Messages
{
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
    }
}
