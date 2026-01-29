using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;
using GD_ControlCenter_WPF.Services.Commands;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    /// <summary>
    /// 三维平台控制服务实现类
    /// </summary>
    public partial class Platform3DService : ObservableObject, IPlatform3DService, IDisposable
    {
        private readonly ISerialPortService _serialPortService;
        private readonly PlatformStorageService _storageService;
        private readonly object _lockObj = new();

        // 暴露给外部的状态
        public PlatformPosition CurrentPosition { get; } = new();
        public PlatformStatus Status { get; } = new();

        public Platform3DService(ISerialPortService serialPortService, PlatformStorageService storageService)
        {
            _serialPortService = serialPortService;
            _storageService = storageService;

            // 订阅来自 ProtocolService 的 8 字节平台响应帧
            WeakReferenceMessenger.Default.Register<Platform3DMessage>(this, (r, m) =>
            {
                HandleHardwareResponse(m.Value);
            });
        }

        /// <summary>
        /// 初始化：从本地 JSON 加载历史坐标
        /// </summary>
        public async Task InitializeAsync()
        {
            var savedData = await _storageService.LoadAsync();
            if (savedData != null)
            {
                CurrentPosition.X = savedData.X;
                CurrentPosition.Y = savedData.Y;
                CurrentPosition.Z = savedData.Z;
                // 同步边界状态
                foreach (var axis in savedData.IsAtMin.Keys)
                {
                    Status.IsAtMin[axis] = savedData.IsAtMin[axis];
                    Status.IsAtMax[axis] = savedData.IsAtMax[axis];
                }
            }
        }

        /// <summary>
        /// 执行异步移动逻辑
        /// </summary>
        public async Task<bool> MoveAxisAsync(AxisType axis, int step, bool isPositive, CancellationToken ct = default)
        {
            // 1. 软件限位与安全检查
            if (!CheckSafety(axis, step, isPositive)) return false;

            lock (_lockObj)
            {
                if (Status.IsMoving) return false;
                Status.IsMoving = true;
            }

            try
            {
                // 2. 发送硬件指令
                SendMoveCommand(axis, step, isPositive);

                // 3. 异步等待移动完成或被取消
                // 注意：此处可结合硬件回传的确认帧进行 TaskCompletionSource 优化，目前先保持逻辑通顺
                await Task.Delay(CalculateMoveDelay(step), ct);

                if (!ct.IsCancellationRequested)
                {
                    UpdateLocalPosition(axis, step, isPositive);
                    await SavePositionAsync();
                    return true;
                }
            }
            catch (OperationCanceledException) { /* 正常取消 */ }
            finally
            {
                Status.IsMoving = false;
            }
            return false;
        }

        /// <summary>
        /// 接收协议层分发的边界信号
        /// </summary>
        public void HandleBoundarySignal(AxisType axis, bool isZeroPosition)
        {
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
            SavePositionAsync().Wait();
        }

        //这需要硬件的更改
        public void StopAll() => Status.IsMoving = false;

        public async Task SavePositionAsync() => await _storageService.SaveAsync(CurrentPosition, Status);

        #region 私有辅助方法

        /// <summary>
        /// 处理硬件返回的 8 字节平台消息
        /// </summary>
        private void HandleHardwareResponse(byte[] rawData)
        {
            if (rawData == null || rawData.Length < 8) return;

            // 根据硬件协议解析轴停止信号 (字节索引 6)
            switch (rawData[6])
            {
                case 0xA1: HandleBoundarySignal(AxisType.X, true); break;  // X 轴零点
                case 0xA3: HandleBoundarySignal(AxisType.X, false); break; // X 轴最大值
                case 0xB1: HandleBoundarySignal(AxisType.Y, true); break;  // Y 轴零点
                case 0xB3: HandleBoundarySignal(AxisType.Y, false); break; // Y 轴最大值
                case 0xC1: HandleBoundarySignal(AxisType.Z, true); break;  // Z 轴零点
                case 0xC3: HandleBoundarySignal(AxisType.Z, false); break; // Z 轴最大值
            }
        }

        /// <summary>
        /// 发送三维平台移动指令
        /// </summary>
        /// <param name="axis">目标轴</param>
        /// <param name="step">移动步长</param>
        /// <param name="isPositive">是否正向移动</param>
        private void SendMoveCommand(AxisType axis, int step, bool isPositive)
        {
            var cmd = ControlCommandFactory.CreatePlatformMove((int)axis, isPositive, step);
            _serialPortService.Send(cmd);
        }

        /// <summary>
        /// 运动前的安全逻辑检查，防止物理撞击或越界
        /// </summary>
        /// <param name="axis">准备移动的轴</param>
        /// <param name="step">移动步长</param>
        /// <param name="isPositive">方向：true 为正向，false 为负向</param>
        /// <returns>检查通过返回 true；若存在撞击风险则返回 false</returns>
        private bool CheckSafety(AxisType axis, int step, bool isPositive)
        {
            // 边界软限制：如果传感器已显示在零点，禁止继续向负方向移动
            if (Status.IsAtMin[axis] && !isPositive) return false;

            // 边界软限制：如果传感器已显示在最大值，禁止继续向正方向移动
            if (Status.IsAtMax[axis] && isPositive) return false;

            // Z 轴特殊安全冗余：探针下压（负向）时，不允许超过预设的安全极限值（如 -2000）
            if (axis == AxisType.Z && !isPositive && (CurrentPosition.Z - step) < PlatformLimits.ZAxisMinLimit)
                return false;

            return true;
        }

        /// <summary>
        /// 更新本地内存中的坐标数值，并重置相关的边界状态标志
        /// </summary>
        /// <param name="axis">移动的轴</param>
        /// <param name="step">移动步长</param>
        /// <param name="isPositive">方向</param>
        private void UpdateLocalPosition(AxisType axis, int step, bool isPositive)
        {
            // 计算位移增量
            int delta = isPositive ? step : -step;

            // 利用索引器更新对应轴的坐标值
            CurrentPosition[axis] += delta;

            // 逻辑自愈：一旦从边界开关处向反方向移动，立即清除对应的边界锁定状态
            if (isPositive) Status.IsAtMin[axis] = false;
            else Status.IsAtMax[axis] = false;
        }

        /// <summary>
        /// 获取指定轴定义的硬限制最大行程步数
        /// </summary>
        /// <param name="axis">轴类型</param>
        /// <returns>该轴的最大步数值</returns>
        private int GetMaxStep(AxisType axis) => axis switch
        {
            AxisType.X => PlatformLimits.MaxStepX,
            AxisType.Y => PlatformLimits.MaxStepY,
            AxisType.Z => PlatformLimits.MaxStepZ,
            _ => 0
        };

        /// <summary>
        /// 根据移动步长估算等待硬件动作完成的时间延迟
        /// </summary>
        /// <param name="step">移动步数</param>
        /// <returns>毫秒值（基准：每 500 步约耗时 1 秒，外加 500ms 冗余）</returns>
        private int CalculateMoveDelay(int step) => (step / 500) * 1000 + 500;

        #endregion

        public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
