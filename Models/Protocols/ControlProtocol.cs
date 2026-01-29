using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Models.Protocols
{
    /// <summary>
    /// 帧结构固定常量
    /// </summary>
    public static class ControlProtocol
    {
        public const byte FrameHeader = 0x68;  // 帧头
        public const byte FrameFooter = 0x16;  // 帧尾
        public const byte DataLength = 0x0D;   // 固定数据长度
        public const int CommandTotalLength = 13; // 完整帧长度
    }

    /// <summary>
    /// 设备通信地址
    /// </summary>
    public enum DeviceAddr : byte
    {
        PC = 0x55,           // 上位机（工控机）
        Controller = 0xAA,   // 主控制板
        SyringePump = 0xBB,  // 注射泵
        PeristalticPump = 0xCC, // 蠕动泵
        Broadcast = 0x00     // 广播地址（所有设备接收）
    }

    /// <summary>
    /// 通信链路类型（定义数据的流向）
    /// </summary>
    public enum CommandType : byte
    {
        PC_To_Controller = 0x01,      // PC 与控制板通信
        Controller_To_Syringe = 0x02, // 控制板与注射泵通信
        Controller_To_Peristaltic = 0x03, // 控制板与蠕动泵通信
        Broadcast = 0x04              // 广播通信
    }

    /// <summary>
    /// 功能码
    /// </summary>
    public enum FunctionCode : byte
    {
        Handshake = 0x11,          // 通信握手
        HighVoltage = 0x22,        // 高压电源设置
        PeristalticSetting = 0x33, // 蠕动泵设置
        SyringeSetting = 0x44,     // 注射泵设置
        ReturnPowerInfo = 0x66,    // 回传电流电压信息
        MultiChannelValve = 0x77,  // 多通道阀控制
        Ignition = 0x88,           // 点火控制
        SteeringValve = 0x99,      // 转向阀（双通道切换）
        Platform3D = 0xFF          // 三维平台移动
    }
}
