using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Services.Commands;
using System.IO;
using System.Text.Json;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    /// <summary>
    /// 三维平台控制服务实现类 (自闭环状态与持久化)
    /// </summary>
    public class Platform3DService : IPlatform3DService, IDisposable
    {
        private readonly ISerialPortService _serialPortService;
        private readonly object _lockObj = new();
        private readonly string _storageFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform_position.json");
        private CancellationTokenSource? _currentMoveCts;

        public PlatformPosition CurrentPosition { get; } = new();
        public PlatformStatus Status { get; } = new();

        public Platform3DService(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;

            // 订阅来自 ProtocolService 的 8 字节平台响应帧
            WeakReferenceMessenger.Default.Register<Platform3DMessage>(this, (r, m) =>
            {
                HandleHardwareResponse(m.Value);
            });
        }

        public async Task InitializeAsync()
        {
            await LoadPositionInternalAsync();
        }

        public async Task<bool> MoveAxisAsync(AxisType axis, int step, bool isPositive, CancellationToken ct = default)
        {
            // 1. 运动前的软限位安全检查
            if (!CheckSafety(axis, step, isPositive)) return false;

            lock (_lockObj)
            {
                if (Status.IsMoving) return false;
                Status.IsMoving = true;
            }

            // 每次移动前，创建一个内部的取消令牌，并与外部传进来的 ct 关联
            _currentMoveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // 2. 发送硬件指令
                SendMoveCommand(axis, step, isPositive);

                // 3. 异步等待移动完成 (【关键修改】：使用 _currentMoveCts.Token)
                // 一旦触碰限位，这里会瞬间抛出 OperationCanceledException
                await Task.Delay(CalculateMoveDelay(step), _currentMoveCts.Token);

                // 4. 如果没抛出异常，说明正常走完了全程没有碰壁，才进行常规的坐标累加
                UpdateLocalPosition(axis, step, isPositive);
                await SavePositionInternalAsync();
                return true;
            }
            catch (OperationCanceledException)
            {
                // 【关键逻辑】：运动被强行打断（碰到物理限位了）。
                // 此时直接返回 false，坚决不执行 UpdateLocalPosition 的累加逻辑！
                return false;
            }
            finally
            {
                // 无论成功还是被打断，立刻释放状态，让界面按钮恢复使能
                Status.IsMoving = false;
                _currentMoveCts?.Dispose();
                _currentMoveCts = null;
            }
        }

        public void StopAll()
        {
            Status.IsMoving = false;
        }

        #region 内部硬件与信号处理逻辑

        private void HandleHardwareResponse(byte[] rawData)
        {
            if (rawData == null || rawData.Length < 8) return;

            // 根据硬件协议解析轴停止信号 (字节索引 6)
            switch (rawData[6])
            {
                case 0xA1: HandleBoundarySignal(AxisType.X, true); break;
                case 0xA3: HandleBoundarySignal(AxisType.X, false); break;
                case 0xB1: HandleBoundarySignal(AxisType.Y, true); break;
                case 0xB3: HandleBoundarySignal(AxisType.Y, false); break;
                case 0xC1: HandleBoundarySignal(AxisType.Z, true); break;
                case 0xC3: HandleBoundarySignal(AxisType.Z, false); break;
            }
        }

        public void HandleBoundarySignal(AxisType axis, bool isZeroPosition)
        {
            // 1. 防抖过滤：如果系统已经知道在限位上了，忽略硬件连发的重复警告
            if (isZeroPosition && Status.IsAtMin[axis]) return;
            if (!isZeroPosition && Status.IsAtMax[axis]) return;

            // 2. 【核心动作】：立即打断当前的移动延时！
            // 这行代码会让 MoveAxisAsync 里的 Task.Delay 瞬间中断，跳入 catch 块。
            _currentMoveCts?.Cancel();

            // 3. 直接将位置锁定为绝对边界值（不加减步长）
            if (isZeroPosition)
            {
                CurrentPosition[axis] = 0;
                Status.IsAtMin[axis] = true;
                Status.IsAtMax[axis] = false;
            }
            else
            {
                CurrentPosition[axis] = GetMaxStep(axis);
                Status.IsAtMin[axis] = false;
                Status.IsAtMax[axis] = true;
            }

            // 4. 异步落盘，保存这个被修正的边界坐标
            _ = SavePositionInternalAsync();

            // 5. 广播高阶业务消息，通知 ViewModel 弹出“已到达边界”的提示信息
            WeakReferenceMessenger.Default.Send(new PlatformBoundaryMessage(axis, isZeroPosition));
        }

        private void SendMoveCommand(AxisType axis, int step, bool isPositive)
        {
            var cmd = ControlCommandFactory.CreatePlatformMove((int)axis, isPositive, step);
            _serialPortService.Send(cmd);
        }

        private bool CheckSafety(AxisType axis, int step, bool isPositive)
        {
            if (Status.IsAtMin[axis] && !isPositive) return false;
            if (Status.IsAtMax[axis] && isPositive) return false;

            if (axis == AxisType.Z && !isPositive && (CurrentPosition.Z - step) < PlatformLimits.ZAxisMinLimit)
                return false;

            return true;
        }

        private void UpdateLocalPosition(AxisType axis, int step, bool isPositive)
        {
            int delta = isPositive ? step : -step;
            CurrentPosition[axis] += delta;

            if (isPositive) Status.IsAtMin[axis] = false;
            else Status.IsAtMax[axis] = false;
        }

        private int GetMaxStep(AxisType axis) => axis switch
        {
            AxisType.X => PlatformLimits.MaxStepX,
            AxisType.Y => PlatformLimits.MaxStepY,
            AxisType.Z => PlatformLimits.MaxStepZ,
            _ => 0
        };

        private int CalculateMoveDelay(int step) => (step / 500) * 1000 + 500;

        #endregion

        #region 本地 JSON 持久化逻辑

        private async Task LoadPositionInternalAsync()
        {
            if (!File.Exists(_storageFilePath)) return;
            try
            {
                string json = await File.ReadAllTextAsync(_storageFilePath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                CurrentPosition.X = root.GetProperty("X").GetInt32();
                CurrentPosition.Y = root.GetProperty("Y").GetInt32();
                CurrentPosition.Z = root.GetProperty("Z").GetInt32();

                var minStatus = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<AxisType, bool>>(root.GetProperty("IsAtMin").GetRawText());
                var maxStatus = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<AxisType, bool>>(root.GetProperty("IsAtMax").GetRawText());

                if (minStatus != null) Status.IsAtMin = minStatus;
                if (maxStatus != null) Status.IsAtMax = maxStatus;
            }
            catch { /* 解析失败保留默认值 */ }
        }

        private async Task SavePositionInternalAsync()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_storageFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var data = new
                {
                    CurrentPosition.X,
                    CurrentPosition.Y,
                    CurrentPosition.Z,
                    Status.IsAtMin,
                    Status.IsAtMax
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_storageFilePath, json);
            }
            catch { /* 写入冲突或异常不阻断控制流 */ }
        }

        #endregion

        public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}