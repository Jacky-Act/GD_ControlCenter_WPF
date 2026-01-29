using GD_ControlCenter_WPF.Models.Platform3D;

namespace GD_ControlCenter_WPF.Services.Platform3D.Logic
{
    /// <summary>
    /// 平台校准逻辑类：负责自动归位及传感器/光谱寻优校准算法
    /// </summary>
    public class CalibrationLogic
    {
        private readonly IPlatform3DService _platformService;

        // 存储各轴计算出的目标位置（步数）
        private readonly Dictionary<AxisType, int> _targetPositions = new()
        {
            { AxisType.X, 0 }, { AxisType.Y, 0 }, { AxisType.Z, 0 }
        };

        public CalibrationLogic(IPlatform3DService platformService)
        {
            _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        }

        /// <summary>
        /// 全轴依次自动归位
        /// 逻辑：按 X -> Y -> Z 顺序向负方向移动，直到触发零点边界信号
        /// </summary>
        /// <param name="parentCts">来自 UI 的全局取消令牌</param>
        public async Task AutoHomeAllAxesAsync(CancellationToken parentCts = default)
        {
            // 定义需要归位的轴序列
            var axesToHome = new List<AxisType> { AxisType.X, AxisType.Y, AxisType.Z };

            try
            {
                foreach (var axis in axesToHome)
                {
                    // 1. 预检查：如果该轴已在零点，则跳过
                    if (_platformService.Status.IsAtMin[axis]) continue;

                    // 2. 超时保护：为每个轴的归位动作设置 30 秒上限，防止传感器失效导致马达受损
                    using var axisTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    // 组合 UI 取消信号和超时信号
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentCts, axisTimeoutCts.Token);

                    // 3. 执行归位移动：向负方向移动“最大行程+冗余量”的步数
                    int homeDistance = PlatformLimits.MaxValidStep + 500;
                    await _platformService.MoveAxisAsync(axis, homeDistance, false, linkedCts.Token);

                    // 4. 轴间停顿：给硬件留出响应时间并降低系统瞬时电流
                    await Task.Delay(500, parentCts);
                }
            }
            catch (OperationCanceledException)
            {
                // 如果用户手动点击停止或超时，确保停止所有电机动作
                _platformService.StopAll();
                throw;
            }
        }

        /// <summary>
        /// 执行单轴自动寻优校准
        /// </summary>
        public async Task CalibrateAxisAsync(AxisType axis, CancellationToken ct = default)
        {
            try
            {
                // 1. 校验前置条件：必须先完成归位
                if (!_platformService.Status.IsAtMin[axis])
                    throw new InvalidOperationException($"{axis}轴未归位，无法开始校准逻辑。");

                // 2. 扫略：向最大值方向移动，此过程会自动触发光谱采集
                int toMaxDistance = PlatformLimits.MaxValidStep + 500;
                await _platformService.MoveAxisAsync(axis, toMaxDistance, true, ct);

                // 3. 计算与等待：给系统留出处理光谱数据的时间
                await Task.Delay(2000, ct);

                // 4. 定位：根据 SetTargetByIntensityTime 计算的目标位置进行折返
                int target = _targetPositions[axis];
                int currentPos = _platformService.CurrentPosition[axis];
                int returnDistance = currentPos - target;

                if (returnDistance > 0)
                    // 向负方向移动回最佳位置
                    await _platformService.MoveAxisAsync(axis, returnDistance, false, ct);
            }
            catch (OperationCanceledException)
            {
                _platformService.StopAll();
                throw;
            }
        }

        /// <summary>
        /// 由外部（如 Coordinator）根据光谱仪采样结果设置目标位置
        /// </summary>
        /// <param name="axis">轴类型</param>
        /// <param name="totalSamplingTimeMs">采集总时长</param>
        /// <param name="maxIntensityTimeMs">峰值出现时间</param>
        public void SetTargetByIntensityTime(AxisType axis, long totalSamplingTimeMs, long maxIntensityTimeMs)
        {
            if (totalSamplingTimeMs <= 0) return;

            // 计算比例：峰值时间 / 总时长
            double ratio = (double)maxIntensityTimeMs / totalSamplingTimeMs;

            // 计算目标步数：当前轴最大行程 * 比例
            int maxDistance = axis switch
            {
                AxisType.X => PlatformLimits.MaxStepX,
                AxisType.Y => PlatformLimits.MaxStepY,
                AxisType.Z => PlatformLimits.MaxStepZ,
                _ => 0
            };

            _targetPositions[axis] = (int)Math.Round(maxDistance * ratio);
        }
    }
}
