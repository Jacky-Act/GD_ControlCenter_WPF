using GD_ControlCenter_WPF.Services.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 通用设备控制服务：负责管理仅需发送指令、无需复杂状态回传的外设
    /// </summary>
    public class GeneralDeviceService
    {
        private readonly ISerialPortService _serialPortService;

        public GeneralDeviceService(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;
        }

        /// <summary>
        /// 控制蠕动泵：封装起停、转向和转速逻辑 
        /// </summary>
        public void ControlPeristalticPump(short speed, bool isClockwise, bool isStart)
        {
            var cmd = ControlCommandFactory.CreatePeristalticPump(speed, isClockwise, isStart);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 控制转向阀：切换到指定通道 
        /// </summary>
        public void ControlSteeringValve(bool channel)
        {
            var cmd = ControlCommandFactory.CreateSteeringValve(channel);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 控制注射泵：设定方向与移动位置 
        /// </summary>
        public void ControlSyringePump(bool isOutput, short distance)
        {
            var cmd = ControlCommandFactory.CreateSyringePump(isOutput, distance);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 控制多通道阀：切换到目标端口 
        /// </summary>
        public void ControlMultiChannelValve(int channel)
        {
            var cmd = ControlCommandFactory.CreateMultiChannelValve(channel);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 触发点火功能：执行 1000ms 延时点火指令
        /// </summary>
        public void Fire()
        {
            var cmd = ControlCommandFactory.CreateIgnition();
            _serialPortService.Send(cmd);
        }
    }
}
