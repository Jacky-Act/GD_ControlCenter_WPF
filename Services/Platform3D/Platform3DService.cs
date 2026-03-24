using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models;
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
        private readonly JsonConfigService _jsonConfigService;
        private readonly object _lockObj = new();
        private readonly string _storageFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform_position.json");
        private CancellationTokenSource? _currentMoveCts;

        public PlatformPosition CurrentPosition { get; } = new();
        public PlatformStatus Status { get; } = new();

        public Platform3DService(ISerialPortService serialPortService, JsonConfigService jsonConfigService)
        {
            _serialPortService = serialPortService;
            _jsonConfigService = jsonConfigService;

            // 在服务实例化时，立即同步加载历史坐标与限位状态
            LoadPositionInternal();

            WeakReferenceMessenger.Default.Register<Platform3DMessage>(this, (r, m) =>
            {
                HandleHardwareResponse(m.Value);
            });
        }

        public Task InitializeAsync()
        {
            LoadPositionInternal(); // 同步读取内存中的配置
            return Task.CompletedTask;
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

        #region 本地 JSON 持久化逻辑 (已迁移至 AppConfig)

        private void LoadPositionInternal()
        {
            var config = _jsonConfigService.Load();

            // 严谨的防空保护
            if (config?.Platform3D == null) return;

            CurrentPosition.X = config.Platform3D.X;
            CurrentPosition.Y = config.Platform3D.Y;
            CurrentPosition.Z = config.Platform3D.Z;

            if (config.Platform3D.IsAtMin != null)
            {
                Status.IsAtMin[AxisType.X] = config.Platform3D.IsAtMin.GetValueOrDefault("X", false);
                Status.IsAtMin[AxisType.Y] = config.Platform3D.IsAtMin.GetValueOrDefault("Y", false);
                Status.IsAtMin[AxisType.Z] = config.Platform3D.IsAtMin.GetValueOrDefault("Z", false);
            }

            if (config.Platform3D.IsAtMax != null)
            {
                Status.IsAtMax[AxisType.X] = config.Platform3D.IsAtMax.GetValueOrDefault("X", false);
                Status.IsAtMax[AxisType.Y] = config.Platform3D.IsAtMax.GetValueOrDefault("Y", false);
                Status.IsAtMax[AxisType.Z] = config.Platform3D.IsAtMax.GetValueOrDefault("Z", false);
            }
        }

        private Task SavePositionInternalAsync()
        {
            var config = _jsonConfigService.Load();

            // 为了安全起见，防止读取旧的 config.json 时 Platform3D 为空
            if (config.Platform3D == null)
                config.Platform3D = new Platform3DConfig();

            config.Platform3D.X = CurrentPosition.X;
            config.Platform3D.Y = CurrentPosition.Y;
            config.Platform3D.Z = CurrentPosition.Z;

            config.Platform3D.IsAtMin["X"] = Status.IsAtMin[AxisType.X];
            config.Platform3D.IsAtMin["Y"] = Status.IsAtMin[AxisType.Y];
            config.Platform3D.IsAtMin["Z"] = Status.IsAtMin[AxisType.Z];

            config.Platform3D.IsAtMax["X"] = Status.IsAtMax[AxisType.X];
            config.Platform3D.IsAtMax["Y"] = Status.IsAtMax[AxisType.Y];
            config.Platform3D.IsAtMax["Z"] = Status.IsAtMax[AxisType.Z];

            // 【修复这里】：将 config 传入 Save 方法
            _ = Task.Run(() => _jsonConfigService.Save(config));

            return Task.CompletedTask;
        }

        #endregion

        public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}