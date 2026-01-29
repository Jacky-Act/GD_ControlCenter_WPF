using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Platform3D;

namespace GD_ControlCenter_WPF.Services.Platform3D.Logic
{
    /// <summary>
    /// 空间分布实验逻辑：采用消息驱动模式，与 UI 和 ViewModel 完全解耦
    /// </summary>
    public class SpatialExperiment
    {
        private readonly IPlatform3DService _platformService;
        private int _currentPointIndex = 0;

        public SpatialExperiment(IPlatform3DService platformService)
        {
            _platformService = platformService;
        }

        public async Task RunAsync(AxisType axis, int moveStep, int acquisitionCount, double intervalSeconds, CancellationToken ct)
        {
            _currentPointIndex = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 1. 物理边界检查
                    if (_platformService.Status.IsAtMax[axis]) break;

                    _currentPointIndex++;

                    // 2. 采样循环
                    for (int i = 0; i < acquisitionCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        // 1. 构造空间分布实验专用数据实体
                        var experimentData = new SpatialExperimentData
                        {
                            PointIndex = _currentPointIndex,
                            AcquisitionRound = i + 1,
                            Position = _platformService.CurrentPosition[axis],
                            Timestamp = DateTime.Now,
                            Axis = axis // 记录当前是哪个轴在做实验
                        };

                        // 2. 通过 Messenger 发送专用的 SpatialExperimentMessage
                        // ViewModel 订阅此消息后，可同时触发 UI 更新与 Excel 写入
                        WeakReferenceMessenger.Default.Send(new SpatialExperimentMessage(experimentData));

                        if (i < acquisitionCount - 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
                        }
                    }

                    // 3. 驱动平台步进
                    bool moveSuccess = await _platformService.MoveAxisAsync(axis, moveStep, true, ct);
                    if (!moveSuccess) break;

                    // 4. 机械稳定延时（后面这里要替换为硬件发回的结束移动逻辑）
                    int settleDelay = (moveStep * 1000) / 500 + 1000;
                    await Task.Delay(settleDelay, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _platformService.StopAll();
                throw;
            }
        }
    }
}
