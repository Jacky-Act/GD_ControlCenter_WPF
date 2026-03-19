using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System;
using System.Collections.ObjectModel;
using System.Linq;

/*
 * 文件名: PeakTrackingService.cs
 * 模块: 业务服务层 (Business Services)
 * 描述: 后台特征峰追踪服务 (全局单例)。
 * 架构角色: 这是一个“无头 (Headless)”的服务。它挂载在消息总线上，只要底层产出了新的光谱数据，
 * 它就会在后台默默计算并更新所有被追踪峰的实时偏移量，供时序图或数据列表直接读取。
 */

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    public class PeakTrackingService
    {
        #region 1. 状态与数据集合

        /// <summary>
        /// 当前系统中所有被用户标记需要追踪的特征峰集合。
        /// 使用 ObservableCollection 是为了方便前端 UI (如侧边栏列表) 直接进行数据绑定。
        /// </summary>
        public ObservableCollection<TrackedPeak> TrackedPeaks { get; } = new ObservableCollection<TrackedPeak>();

        #endregion

        #region 2. 初始化与消息订阅

        /// <summary>
        /// 构造函数。
        /// 在系统启动注入为单例时，自动注册对全局光谱数据的监听。
        /// </summary>
        public PeakTrackingService()
        {
            // 只要一有新的光谱数据到来，就立刻更新所有的峰位坐标
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                UpdatePeaksWithNewData(m.Value);
            });
        }

        #endregion

        #region 3. 追踪点管理 (CRUD)

        /// <summary>
        /// 添加一个新的追踪峰。
        /// 通常由主界面的“图表右键菜单”或“快捷键”触发。
        /// </summary>
        /// <param name="targetWavelength">鼠标点击处的理论波长</param>
        public void AddPeak(double targetWavelength)
        {
            // 防呆校验：如果该位置 0.5nm 内已经存在一个追踪峰，则视为重复点击，直接忽略
            if (TrackedPeaks.Any(p => Math.Abs(p.BaseWavelength - targetWavelength) < 0.5))
            {
                return;
            }

            TrackedPeaks.Add(new TrackedPeak(targetWavelength));
        }

        /// <summary>
        /// 移除距离鼠标点击位置最近的一个峰。
        /// </summary>
        /// <param name="clickWavelength">鼠标点击处的理论波长</param>
        /// <param name="clickTolerance">判定点击生效的物理像素宽容度（默认±5nm）</param>
        public void RemovePeakNear(double clickWavelength, double clickTolerance = 5.0)
        {
            if (TrackedPeaks.Count == 0) return;

            // 1. 遍历计算所有峰距离鼠标点击位置的绝对偏差
            var closestPeak = TrackedPeaks
                .Select(p => new { Peak = p, Distance = Math.Abs(p.CurrentWavelength - clickWavelength) })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            // 2. 如果最近的那个峰也在“宽容度”范围内，则将其抹杀
            if (closestPeak != null && closestPeak.Distance <= clickTolerance)
            {
                TrackedPeaks.Remove(closestPeak.Peak);
            }
        }

        /// <summary>
        /// 一键清除所有追踪峰。
        /// 通常在时序图关闭，或切换项目时调用。
        /// </summary>
        public void ClearAll()
        {
            TrackedPeaks.Clear();
        }

        #endregion

        #region 4. 核心刷新引擎

        /// <summary>
        /// 核心刷新逻辑：驱动底层数学算法，更新所有追踪峰的“真实偏移量”。
        /// </summary>
        /// <param name="currentData">由底层刚采上来的最新一帧光谱数据</param>
        public void UpdatePeaksWithNewData(SpectralData currentData)
        {
            // 校验数据有效性
            if (currentData == null || currentData.Wavelengths == null || TrackedPeaks.Count == 0)
                return;

            foreach (var peak in TrackedPeaks)
            {
                // 步骤 1：在初始基准波长 (BaseWavelength) 附近的容差窗口内，找到真实的光强最高点波长
                double realX = SpectrometerLogic.GetActualPeakWavelength(currentData, peak.BaseWavelength, peak.ToleranceWindow);

                // 步骤 2：获取该真实波长对应光强计数
                double realY = SpectrometerLogic.GetIntensityAtWavelength(currentData, realX);

                // 步骤 3：更新模型坐标
                // 注意：TrackedPeak 继承自 ObservableObject，此处赋值会瞬间触发 PropertyChanged，
                // 任何绑定了这两个属性的 UI 控件 (如主界面的竖线、时序图的缓存拦截器) 都会随之同步。
                peak.CurrentWavelength = realX;
                peak.CurrentIntensity = realY;
            }
        }

        #endregion
    }
}