using GD_ControlCenter_WPF.Services.Commands;

/*
 * 文件名: GeneralDeviceService.cs
 * 描述: 通用外设控制服务。
 * 该服务管理那些“仅需发送单向控制指令、无需复杂状态闭环回传”的外设。
 * 涵盖了蠕动泵、注射泵、多通道阀、转向阀以及点火功能的逻辑触发。
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 通用设备服务类。
    /// 充当业务逻辑与指令工厂（Factory）之间的桥梁。
    /// </summary>
    public class GeneralDeviceService
    {
        private readonly ISerialPortService _serialPortService;

        public GeneralDeviceService(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;
        }

        /// <summary>
        /// 控制蠕动泵动作。
        /// </summary>
        /// <param name="speed">目标转速</param>
        /// <param name="isClockwise">转向（True: 顺时针, False: 逆时针）</param>
        /// <param name="isStart">是否开启运行</param>
        public void ControlPeristalticPump(short speed, bool isClockwise, bool isStart)
        {
            var cmd = ControlCommandFactory.CreatePeristalticPump(speed, isClockwise, isStart);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 控制转向阀。
        /// </summary>
        /// <param name="channel">目标状态（True 对应状态 2，False 对应状态 1）</param>
        public void ControlSteeringValve(bool channel)
        {
            var cmd = ControlCommandFactory.CreateSteeringValve(channel);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 控制注射泵。
        /// </summary>
        /// <param name="isOutput">方向（True: 推出, False: 吸入）</param>
        /// <param name="distance">位移步数（0-3000）</param>
        public void ControlSyringePump(bool isOutput, short distance)
        {
            var cmd = ControlCommandFactory.CreateSyringePump(isOutput, distance);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 控制多通道切换阀。
        /// </summary>
        /// <param name="channel">目标通道号（1-10）</param>
        public void ControlMultiChannelValve(int channel)
        {
            var cmd = ControlCommandFactory.CreateMultiChannelValve(channel);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 触发点火功能。
        /// 调用封装好的 1000ms 延时点火指令。
        /// </summary>
        public void Fire()
        {
            var cmd = ControlCommandFactory.CreateIgnition();
            _serialPortService.Send(cmd);
        }
    }
}
