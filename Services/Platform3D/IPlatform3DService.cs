using GD_ControlCenter_WPF.Models.Platform3D;

namespace GD_ControlCenter_WPF.Services.Platform3D
{
    /// <summary>
    /// 三维平台基础控制服务接口
    /// 核心职责：负责单轴的物理移动、坐标实时维护及边界安全状态管理
    /// </summary>
    public interface IPlatform3DService
    {
        // --- 状态暴露 ---

        /// <summary>
        /// 平台当前实时坐标
        /// </summary>
        PlatformPosition CurrentPosition { get; }

        /// <summary>
        /// 平台当前运行状态（是否移动、边界标志等）
        /// </summary>
        PlatformStatus Status { get; }

        // --- 核心动作 ---

        /// <summary>
        /// 初始化平台：加载持久化坐标并同步状态
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// 异步移动指定轴
        /// </summary>
        /// <param name="axis">目标轴 (X/Y/Z)</param>
        /// <param name="step">移动步进值</param>
        /// <param name="isPositive">方向：true 为正向，false 为负向</param>
        /// <param name="ct">取消令牌，用于手动停止或实验终止</param>
        /// <returns>移动是否成功执行</returns>
        Task<bool> MoveAxisAsync(AxisType axis, int step, bool isPositive, CancellationToken ct = default);

        /// <summary>
        /// 立即停止所有轴的移动
        /// </summary>
        void StopAll();

        /// <summary>
        /// 手动更新并保存当前坐标
        /// </summary>
        Task SavePositionAsync();

        // --- 内部逻辑接口（供 ProtocolService 调用） ---

        /// <summary>
        /// 处理从硬件回传的边界/停止信号
        /// </summary>
        /// <param name="axis">停止的轴</param>
        /// <param name="isZeroPosition">是否到达零点（Min）</param>
        void HandleBoundarySignal(AxisType axis, bool isZeroPosition);
    }
}
