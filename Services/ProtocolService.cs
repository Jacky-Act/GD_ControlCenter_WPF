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
                // --- 1. 电池协议识别 (DD 03 ... 77), 长度 34 ---
                if (_buffer[0] == 0xDD && _buffer.Count >= 2 && _buffer[1] == 0x03)
                {
                    if (_buffer.Count < 34) break; // 数据包不全，等待后续

                    if (_buffer[33] == 0x77) // 验证帧尾
                    {
                        var frame = _buffer.Take(34).ToArray();
                        WeakReferenceMessenger.Default.Send(new BatteryFrameMessage(frame));
                        _buffer.RemoveRange(0, 34);
                        continue;
                    }
                }

                // --- 2. 控制协议族识别 (以 0x68 开头) ---
                if (_buffer[0] == ControlProtocol.FrameHeader)
                {
                    // 2.1 三维平台响应 (8 字节格式)
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

                    // 2.2 标准控制响应 (13 字节格式)
                    if (_buffer.Count >= ControlProtocol.CommandTotalLength)
                    {
                        if (_buffer[ControlProtocol.CommandTotalLength - 1] == ControlProtocol.FrameFooter)
                        {
                            var frame = _buffer.Take(ControlProtocol.CommandTotalLength).ToArray();

                            // 执行 CRC 校验确保数据准确
                            byte[] crc = CrcHelper.Compute(frame, 10);

                            if (frame[10] == crc[0] && frame[11] == crc[1])
                            {
                                WeakReferenceMessenger.Default.Send(new ControlResponseMessage(frame));
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
