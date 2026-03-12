using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Helpers;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Protocols;

namespace GD_ControlCenter_WPF.Services
{
    /// <summary>
    /// 协议解析服务：集中处理所有硬件回传数据，实现拼包、校验与消息分发
    /// </summary>
    public class ProtocolService
    {
        private readonly List<byte> _buffer = new();

        public ProtocolService()
        {
            // 订阅串口收到的原始 Hex 数据
            WeakReferenceMessenger.Default.Register<HexDataMessage>(this, (r, m) =>
            {
                lock (_buffer)
                {
                    OnDataReceived(m.Value);
                }
            });
        }

        private void OnDataReceived(byte[] data)
        {
            _buffer.AddRange(data);

            while (_buffer.Count > 0)
            {
                // --- 控制协议族识别 (以 0x68 开头) ---
                if (_buffer[0] == ControlProtocol.FrameHeader)
                {
                    // 三维平台响应 (8 字节格式)
                    if (_buffer.Count >= 4 && (FunctionCode)_buffer[3] == FunctionCode.Platform3D)
                    {
                        const int platformFrameLength = 8;
                        if (_buffer.Count < platformFrameLength) break;

                        if (_buffer[platformFrameLength - 1] == ControlProtocol.FrameFooter)
                        {
                            var frame = _buffer.Take(platformFrameLength).ToArray();
                            WeakReferenceMessenger.Default.Send(new Platform3DMessage(frame));
                            _buffer.RemoveRange(0, platformFrameLength);
                            continue;
                        }
                    }

                    // 标准控制响应 (13 字节格式)
                    if (_buffer.Count >= ControlProtocol.CommandTotalLength)
                    {
                        if (_buffer[ControlProtocol.CommandTotalLength - 1] == ControlProtocol.FrameFooter)
                        {
                            var frame = _buffer.Take(ControlProtocol.CommandTotalLength).ToArray();

                            // 执行 CRC 校验确保数据准确
                            byte[] crc = CrcHelper.Compute(frame, 10);

                            if (frame[10] == crc[0] && frame[11] == crc[1])
                            {
                                // 通过功能码区分心跳状态数据与常规指令回执
                                if (frame[3] == (byte)FunctionCode.Battery) // 假设已定义 Battery = 0xEE
                                {
                                    // 仅发送给 BatteryService
                                    WeakReferenceMessenger.Default.Send(new BatteryFrameMessage(frame));
                                }
                                else
                                {
                                    // 发送给其他通用控制逻辑
                                    WeakReferenceMessenger.Default.Send(new ControlResponseMessage(frame));
                                }

                                _buffer.RemoveRange(0, ControlProtocol.CommandTotalLength);
                                continue;
                            }
                        }
                    }
                }

                // 如果缓冲区第一个字节无法匹配任何已知协议头，则视为干扰数据并移除
                _buffer.RemoveAt(0);
            }
        }
    }
}
