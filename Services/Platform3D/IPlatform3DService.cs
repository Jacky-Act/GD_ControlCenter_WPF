using GD_ControlCenter_WPF.Models.Platform3D;
using System.Threading;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    /// <summary>
    /// 三维平台基础控制服务接口
    /// </summary>
    public interface IPlatform3DService
    {
        /// <summary>
        /// 平台当前实时坐标 (纯数据模型)
        /// </summary>
        PlatformPosition CurrentPosition { get; }

        /// <summary>
        /// 平台当前运行状态 (纯数据模型)
        /// </summary>
        PlatformStatus Status { get; }

        /// <summary>
        /// 初始化平台：加载持久化坐标并同步状态
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// 异步移动指定轴
        /// </summary>
        Task<bool> MoveAxisAsync(AxisType axis, int step, bool isPositive, CancellationToken ct = default);

        /// <summary>
        /// 立即停止所有轴的移动标志复位
        /// </summary>
        void StopAll();
    }
}