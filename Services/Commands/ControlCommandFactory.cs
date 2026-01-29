using GD_ControlCenter_WPF.Helpers;
using GD_ControlCenter_WPF.Models.Protocols;

namespace GD_ControlCenter_WPF.Services.Commands
{
    /// <summary>
    /// 控制指令工厂：负责将业务参数转化为符合硬件协议的 13 字节指令
    /// </summary>
    public static class ControlCommandFactory
    {
        #region 核心包装逻辑

        /// <summary>
        /// 通用帧包装方法：自动处理帧头、长度、保留位、CRC、帧尾
        /// </summary>
        private static byte[] PackFrame(DeviceAddr addr, CommandType type, FunctionCode func, byte[] dataSegment)
        {
            byte[] frame = new byte[ControlProtocol.CommandTotalLength];

            // 填充固定协议头
            frame[0] = ControlProtocol.FrameHeader; // 0x68
            frame[1] = (byte)addr;
            frame[2] = (byte)type;
            frame[3] = (byte)func;
            frame[4] = ControlProtocol.DataLength;  // 0x0D
            frame[5] = 0x00;                        // 保留位

            // 填充 4 字节数据段 (索引 6-9)
            if (dataSegment != null && dataSegment.Length == 4)
                Array.Copy(dataSegment, 0, frame, 6, 4);

            // 计算并填充 CRC (计算前 10 字节)
            byte[] crc = CrcHelper.Compute(frame, 10);
            frame[10] = crc[0]; // CRC 高位
            frame[11] = crc[1]; // CRC 低位

            // 填充帧尾
            frame[12] = ControlProtocol.FrameFooter; // 0x16

            return frame;
        }

        #endregion

        #region 具体业务指令生成

        /// <summary>
        /// 生成高压电源控制命令
        /// </summary>
        /// <param name="voltage">电压值</param>
        /// <param name="current">电流值</param>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreateHighVoltage(int voltage, int current)
        {
            byte[] data = new byte[4];
            data[0] = (byte)(voltage % 256); // 电压低位
            data[1] = (byte)(voltage / 256); // 电压高位
            data[2] = (byte)(current % 256); // 电流低位
            data[3] = (byte)(current / 256); // 电流高位

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.HighVoltage, data);
        }

        /// <summary>
        /// 生成蠕动泵控制命令
        /// </summary>
        /// <param name="speed">转速 (0-100)</param>
        /// <param name="isClockwise">是否顺时针 顺时针该字节是1，逆时针是0</param> 
        /// <param name="isStart">是否启动</param>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreatePeristalticPump(short speed, bool isClockwise, bool isStart)
        {
            // 强制将速度限制在 0-100 之间
            short safeSpeed = Math.Clamp(speed, (short)0, (short)100);

            byte[] data = new byte[4];
            data[0] = (byte)(isStart ? 1 : 0);
            data[1] = (byte)(isClockwise ? 1 : 0);
            short scaledSpeed = (short)(speed * 100);
            data[2] = (byte)(scaledSpeed / 256);
            data[3] = (byte)(scaledSpeed % 256);

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.PeristalticSetting, data);
        }

        /// <summary>
        /// 生成转向阀控制命令
        /// </summary>
        /// <param name="channel">通道号 (1 或 2)</param>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreateSteeringValve(int channel)
        {
            byte[] data = new byte[4] { (byte)channel, 0, 0, 0 };

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.SteeringValve, data);
        }

        /// <summary>
        /// 生成转向阀控制命令
        /// </summary>
        /// <param name="isChannel2">是否切换至状态 2 (true: 状态 2, false: 状态 1)</param>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreateSteeringValve(bool isChannel2)
        {
            byte channel = (byte)(isChannel2 ? 0x02 : 0x01);
            byte[] data = new byte[4] { channel, 0, 0, 0 };

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.SteeringValve, data);
        }

        /// <summary>
        /// 生成注射泵控制命令
        /// </summary>
        /// <param name="isOutput">移动方向（true=输出，false=输入）</param>
        /// <param name="distance">移动距离（0-3000）</param>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreateSyringePump(bool isOutput, short distance)
        {
            // 安全校验：强制限制在 0-3000 之间
            short safeDistance = Math.Clamp(distance, (short)0, (short)3000);

            byte[] data = new byte[4];
            data[0] = (byte)(isOutput ? 1 : 0);
            data[1] = (byte)(safeDistance / 256); // 高 8 位
            data[2] = (byte)(safeDistance % 256); // 低 8 位
            data[3] = 0x00;

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.SyringeSetting, data);
        }

        /// <summary>
        /// 生成三维平台移动命令
        /// </summary>
        /// <param name="dimension">轴维度（1=X, 2=Y, 3=Z）</param>
        /// <param name="isPositive">是否正向移动 (true: 正向[1], false: 反向[0])</param>
        /// <param name="step">步长（1~60000）</param>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreatePlatformMove(int dimension, bool isPositive, int step)
        {
            byte[] data = new byte[4];
            data[0] = (byte)dimension;
            data[1] = (byte)(isPositive ? 0x01 : 0x00);
            data[2] = (byte)(step % 256); // 低字节
            data[3] = (byte)(step / 256); // 高字节

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.Platform3D, data);
        }

        /// <summary>
        /// 生成多通道阀控制命令
        /// </summary>
        /// <param name="channel">目标通道（1-10）</param>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreateMultiChannelValve(int channel)
        {
            // 强制限制在 1-10 之间
            int safeChannel = Math.Clamp(channel, 1, 10);
            byte[] data = new byte[4] { (byte)safeChannel, 0x00, 0x00, 0x00 };

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.MultiChannelValve, data);
        }

        /// <summary>
        /// 生成点火功能命令
        /// 逻辑：蠕动泵逆时针启动，固定点火时间 1000ms；数据格式：00 01 延时低字节 延时高字节
        /// </summary>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreateIgnition()
        {
            const int fireTimeMs = 1000; // 固定点火时间为 1000ms

            byte[] data = new byte[4];
            data[0] = 0x00;
            data[1] = 0x00;
            data[2] = (byte)(fireTimeMs % 256); // 1000 的低位为 0xE8
            data[3] = (byte)(fireTimeMs / 256); // 1000 的高位为 0x03

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.Ignition, data);
        }

        /// <summary>
        /// 生成高压电源电压电流信息查询命令
        /// </summary>
        /// <returns>符合控制协议的 13 字节指令包</returns>
        public static byte[] CreateHighVoltageQuery()
        {
            byte[] data = new byte[4] { 0x00, 0x00, 0x00, 0x00 };

            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.ReturnPowerInfo, data);
        }

        /// <summary>
        /// 生成电池状态查询命令
        /// 协议固定序列：DD A5 03 00 FF FD 77
        /// </summary>
        /// <returns>7 字节查询指令</returns>
        public static byte[] CreateBatteryQuery()
        {
            return new byte[] { 0xDD, 0xA5, 0x03, 0x00, 0xFF, 0xFD, 0x77 };
        }

        #endregion
    }
}
