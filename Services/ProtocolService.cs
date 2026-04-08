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
            // 将新数据追加至缓冲区末尾
            _buffer.AddRange(data);

            // 循环扫描缓冲区，直至无法解析出完整帧
            while (_buffer.Count > 0)
            {
                // 寻找帧起始符 (0x68)
                if (_buffer[0] == ControlProtocol.FrameHeader)
                {
                    // --- 分支 A: 三维平台反馈帧 (固定 8 字节) ---
                    if (_buffer.Count >= 4 && (FunctionCode)_buffer[3] == FunctionCode.Platform3D)
                    {
                        const int platformFrameLength = 8;

                        // 若缓冲区不足 8 字节，等待更多数据到达
                        if (_buffer.Count < platformFrameLength) break;

                        // 验证帧尾是否合法
                        if (_buffer[platformFrameLength - 1] == ControlProtocol.FrameFooter)
                        {
                            byte[] frame = _buffer.Take(platformFrameLength).ToArray();

                            // 解析成功，分发三维平台专用消息
                            WeakReferenceMessenger.Default.Send(new Platform3DMessage(frame));

                            // 从缓冲区移除已处理的报文
                            _buffer.RemoveRange(0, platformFrameLength);
                            continue;
                        }
                    }

                    // --- 分支 B: 标准控制响应帧 (固定 13 字节) ---
                    if (_buffer.Count >= ControlProtocol.CommandTotalLength)
                    {
                        // 验证帧尾 (索引 12)
                        if (_buffer[ControlProtocol.CommandTotalLength - 1] == ControlProtocol.FrameFooter)
                        {
                            byte[] frame = _buffer.Take(ControlProtocol.CommandTotalLength).ToArray();

                            // 计算并核对 CRC 校验码 (计算范围 0-9 字节)
                            byte[] crc = CrcHelper.Compute(frame, 10);

                            if (frame[10] == crc[0] && frame[11] == crc[1])
                            {
                                // 根据功能码进行二级路由分发
                                byte code = frame[3];

                                if (code == (byte)FunctionCode.Battery)
                                {
                                    // 转发至电池状态监控模块
                                    WeakReferenceMessenger.Default.Send(new BatteryFrameMessage(frame));
                                }
                                else
                                {
                                    // 转发至通用硬件响应处理模块（如高压电源、泵组回执）
                                    WeakReferenceMessenger.Default.Send(new ControlResponseMessage(frame));
                                }

                                _buffer.RemoveRange(0, ControlProtocol.CommandTotalLength);
                                continue;
                            }
                        }
                    }
                }

                // 干扰数据处理：若当前字节无法构成有效协议头，则逐字节剔除
                _buffer.RemoveAt(0);
            }
        }
    }
}
