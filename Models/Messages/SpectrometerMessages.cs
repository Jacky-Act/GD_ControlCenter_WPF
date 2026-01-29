using CommunityToolkit.Mvvm.Messaging.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;

namespace GD_ControlCenter_WPF.Models.Messages
{
    namespace GD_ControlCenter_WPF.Models.Messages
    {
        /// <summary>
        /// 单个光谱仪数据消息：负责传输单次采样或持续测量的光谱实体。
        /// 订阅者：实时波形图 ViewModel、光谱处理逻辑类（SpectrometerLogic）。
        /// </summary>
        public class SpectralDataMessage : ValueChangedMessage<SpectralData>
        {
            /// <summary>
            /// 初始化 <see cref="SpectralDataMessage"/> 类的新实例。
            /// </summary>
            /// <param name="value">包含波长、强度及设备序列号的光谱数据实体。</param>
            public SpectralDataMessage(SpectralData value) : base(value)
            {
            }
        }

        /// <summary>
        /// 组合/拼接光谱消息：负责传输由多台光谱仪拼接后的全谱数据。
        /// 订阅者：全谱预览界面 ViewModel、实验数据记录模块。
        /// </summary>
        public class CombinedSpectralMessage : ValueChangedMessage<SpectralData>
        {
            /// <summary>
            /// 初始化 <see cref="CombinedSpectralMessage"/> 类的新实例。
            /// </summary>
            /// <param name="value">拼接去重后的完整光谱实体。</param>
            public CombinedSpectralMessage(SpectralData value) : base(value)
            {
            }
        }

        /// <summary>
        /// 光谱仪状态变更消息：当设备连接、断开或发生饱和报警时发送。
        /// 订阅者：状态栏 ViewModel、系统日志模块。
        /// </summary>
        public class SpectrometerStatusMessage : ValueChangedMessage<SpectrometerConfig>
        {
            /// <summary>
            /// 初始化 <see cref="SpectrometerStatusMessage"/> 类的新实例。
            /// </summary>
            /// <param name="value">当前设备的配置与状态快照。</param>
            public SpectrometerStatusMessage(SpectrometerConfig value) : base(value)
            {
            }
        }
    }
}
