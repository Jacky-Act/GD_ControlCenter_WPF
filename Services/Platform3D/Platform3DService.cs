using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Services.Commands;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    public partial class Platform3DService : ObservableObject, IPlatform3DService, IDisposable
    {
        private readonly ISerialPortService _serialPortService;
        private readonly PlatformStorageService _storageService;
        private readonly object _lockObj = new();

        // 【关键改动】引入取消令牌，用于碰到限位时瞬间中止 Task.Delay
        private CancellationTokenSource? _currentMoveCts;

        public PlatformPosition CurrentPosition { get; } = new();
        public PlatformStatus Status { get; } = new();

        public Platform3DService(ISerialPortService serialPortService, PlatformStorageService storageService)
        {
            _serialPortService = serialPortService;
            _storageService = storageService;

            WeakReferenceMessenger.Default.Register<Platform3DMessage>(this, (r, m) =>
            {
                HandleHardwareResponse(m.Value);
            });
        }

        public async Task InitializeAsync()
        {
            var savedData = await _storageService.LoadAsync();
            if (savedData != null)
            {
                CurrentPosition.X = savedData.X;
                CurrentPosition.Y = savedData.Y;
                CurrentPosition.Z = savedData.Z;
                foreach (var axis in savedData.IsAtMin.Keys)
                {
                    Status.IsAtMin[axis] = savedData.IsAtMin[axis];
                    Status.IsAtMax[axis] = savedData.IsAtMax[axis];
                }
                // 恢复 Z 轴零点确立状态
                if (Status.IsAtMin[AxisType.Z]) Status.HasReceivedZZero = true;
            }
        }

        public async Task<bool> MoveAxisAsync(AxisType axis, int step, bool isPositive, CancellationToken ct = default)
        {
            if (!CheckSafety(axis, step, isPositive)) return false;

            lock (_lockObj)
            {
                if (Status.IsMoving) return false;
                Status.IsMoving = true;
            }

            // 【关键改动】将传入的 ct 和内部的限位取消逻辑关联
            _currentMoveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                SendMoveCommand(axis, step, isPositive);

                // 使用令牌等待。如果 HandleBoundarySignal 被调用，这里会立即抛出异常并结束，不再傻等。
                await Task.Delay(CalculateMoveDelay(step), _currentMoveCts.Token);

                if (!_currentMoveCts.IsCancellationRequested)
                {
                    UpdateLocalPosition(axis, step, isPositive);
                    await SavePositionAsync();
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                // 碰到物理限位或手动停止，直接返回 false，不再累加坐标
                return false;
            }
            finally
            {
                Status.IsMoving = false;
                _currentMoveCts?.Dispose();
                _currentMoveCts = null;
            }
            return false;
        }

        public void HandleBoundarySignal(AxisType axis, bool isZeroPosition)
        {
            // 【关键改动】一旦碰到限位，立即中止 MoveAxisAsync 里的 Task.Delay
            _currentMoveCts?.Cancel();

            if (isZeroPosition)
            {
                CurrentPosition[axis] = 0;
                Status.IsAtMin[axis] = true;
                Status.IsAtMax[axis] = false;
                // 核心：确立 Z 轴零点
                if (axis == AxisType.Z) Status.HasReceivedZZero = true;
            }
            else
            {
                CurrentPosition[axis] = GetMaxStep(axis);
                Status.IsAtMin[axis] = false;
                Status.IsAtMax[axis] = true;
            }
            SavePositionAsync().Wait();

            // 发送消息通知 UI 弹窗（同事的功能）
            WeakReferenceMessenger.Default.Send(new PlatformBoundaryMessage(axis, isZeroPosition));
        }

        private bool CheckSafety(AxisType axis, int step, bool isPositive)
        {
            if (Status.IsAtMin[axis] && !isPositive) return false;
            if (Status.IsAtMax[axis] && isPositive) return false;

            // Z 轴特殊负向软限位检查
            if (axis == AxisType.Z && !isPositive && Status.HasReceivedZZero)
            {
                if ((CurrentPosition.Z - step) < PlatformLimits.ZAxisMinLimit) return false;
            }
            return true;
        }

        private void UpdateLocalPosition(AxisType axis, int step, bool isPositive)
        {
            int delta = isPositive ? step : -step;
            CurrentPosition[axis] += delta;
            if (isPositive) Status.IsAtMin[axis] = false;
            else Status.IsAtMax[axis] = false;
        }

        public void StopAll() => Status.IsMoving = false;
        public async Task SavePositionAsync() => await _storageService.SaveAsync(CurrentPosition, Status);
        private void SendMoveCommand(AxisType axis, int step, bool isPositive) => _serialPortService.Send(ControlCommandFactory.CreatePlatformMove((int)axis, isPositive, step));
        private int GetMaxStep(AxisType axis) => axis switch { AxisType.X => PlatformLimits.MaxStepX, AxisType.Y => PlatformLimits.MaxStepY, AxisType.Z => PlatformLimits.MaxStepZ, _ => 0 };
        private int CalculateMoveDelay(int step) => (step / 500) * 1000 + 300;

        public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
        #region 硬件协议解析

        /// <summary>
        /// 处理硬件返回的 8 字节平台消息 (核心：解析限位信号)
        /// </summary>
        private void HandleHardwareResponse(byte[] rawData)
        {
            if (rawData == null || rawData.Length < 8) return;

            // 根据底层协议解析轴停止信号 (字节索引 6)
            // A1,A3=X轴, B1,B3=Y轴, C1,C3=Z轴
            switch (rawData[6])
            {
                case 0xA1: HandleBoundarySignal(AxisType.X, true); break;  // X轴零点
                case 0xA3: HandleBoundarySignal(AxisType.X, false); break; // X轴最大值
                case 0xB1: HandleBoundarySignal(AxisType.Y, true); break;  // Y轴零点
                case 0xB3: HandleBoundarySignal(AxisType.Y, false); break; // Y轴最大值
                case 0xC1: HandleBoundarySignal(AxisType.Z, true); break;  // Z轴零点
                case 0xC3: HandleBoundarySignal(AxisType.Z, false); break; // Z轴最大值
            }
        }

        #endregion
    }
}