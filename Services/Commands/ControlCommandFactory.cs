using GD_ControlCenter_WPF.Helpers;
using GD_ControlCenter_WPF.Models.Protocols;

namespace GD_ControlCenter_WPF.Services.Commands
{
    public static class ControlCommandFactory
    {
        private static byte[] PackFrame(DeviceAddr addr, CommandType type, FunctionCode func, byte[] dataSegment)
        {
            byte[] frame = new byte[ControlProtocol.CommandTotalLength];
            frame[0] = ControlProtocol.FrameHeader;
            frame[1] = (byte)addr;
            frame[2] = (byte)type;
            frame[3] = (byte)func;
            frame[4] = ControlProtocol.DataLength;
            frame[5] = 0x00;
            if (dataSegment != null && dataSegment.Length == 4) Array.Copy(dataSegment, 0, frame, 6, 4);
            byte[] crc = CrcHelper.Compute(frame, 10);
            frame[10] = crc[0];
            frame[11] = crc[1];
            frame[12] = ControlProtocol.FrameFooter;
            return frame;
        }

        public static byte[] CreateHighVoltage(int voltage, int current)
        {
            byte[] data = new byte[4];
            data[0] = (byte)(voltage % 256); data[1] = (byte)(voltage / 256);
            data[2] = (byte)(current % 256); data[3] = (byte)(current / 256);
            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.HighVoltage, data);
        }

        public static byte[] CreatePeristalticPump(short speed, bool isClockwise, bool isStart)
        {
            short safeSpeed = Math.Clamp(speed, (short)0, (short)100);
            byte[] data = new byte[4];
            data[0] = (byte)(isStart ? 1 : 0); data[1] = (byte)(isClockwise ? 1 : 0);
            short scaledSpeed = (short)(safeSpeed * 100);
            data[2] = (byte)(scaledSpeed / 256); data[3] = (byte)(scaledSpeed % 256);
            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.PeristalticSetting, data);
        }

        public static byte[] CreateSteeringValve(bool isChannel2)
        {
            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.SteeringValve, new byte[] { (byte)(isChannel2 ? 0x02 : 0x01), 0, 0, 0 });
        }

        public static byte[] CreateSyringePump(bool isOutput, short distance)
        {
            short safeDist = Math.Clamp(distance, (short)0, (short)3000);
            byte[] data = new byte[4];
            data[0] = (byte)(isOutput ? 1 : 0); data[1] = (byte)(safeDist / 256); data[2] = (byte)(safeDist % 256); data[3] = 0x00;
            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.SyringeSetting, data);
        }

        public static byte[] CreatePlatformMove(int dimension, bool isPositive, int step)
        {
            byte[] data = new byte[4];
            data[0] = (byte)dimension; data[1] = (byte)(isPositive ? 0x01 : 0x00);
            data[2] = (byte)(step % 256); data[3] = (byte)(step / 256);
            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.Platform3D, data);
        }

        public static byte[] CreateMultiChannelValve(int channel)
        {
            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.MultiChannelValve, new byte[] { (byte)Math.Clamp(channel, 1, 10), 0, 0, 0 });
        }

        public static byte[] CreateIgnition(int delayMs)
        {
            byte[] data = new byte[4];
            data[2] = (byte)(delayMs % 256); data[3] = (byte)(delayMs / 256);
            return PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.Ignition, data);
        }

        public static byte[] CreateHighVoltageQuery() => PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.ReturnPowerInfo, new byte[4]);
        public static byte[] CreateBatteryQuery() => PackFrame(DeviceAddr.Controller, CommandType.PC_To_Controller, FunctionCode.Battery, new byte[4]);
    }
}
