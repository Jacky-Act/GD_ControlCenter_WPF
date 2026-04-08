using CommunityToolkit.Mvvm.ComponentModel;

/*
 * 文件名: PlatformModel.cs
 * 描述: 本文件定义了三维运动平台的物理模型、运行状态以及软限位常量。
 * 采用了 CommunityToolkit.Mvvm 框架实现属性变更通知，确保位置与状态数据能实时同步至 UI 界面。
 * 同时也整合了硬件通信协议中定义的轴类型映射关系。
 * 项目: GD_ControlCenter_WPF
 */

namespace GD_ControlCenter_WPF.Models.Platform3D
{
    /// <summary>
    /// 三维平台轴类型枚举
    /// 直接映射硬件协议：X=1, Y=2, Z=3
    /// </summary>
    public enum AxisType : byte
    {
        X = 0x01,
        Y = 0x02,
        Z = 0x03
    }

    /// <summary>
    /// 三维平台实时位置模型。
    /// 继承自 ObservableObject，利用 [ObservableProperty] 自动生成适配 WPF 绑定的属性。
    /// 用于记录和展示 X、Y、Z 三轴当前的脉冲步数。
    /// </summary>
    public partial class PlatformPosition : ObservableObject
    {
        [ObservableProperty]
        private int _x;

        [ObservableProperty]
        private int _y;

        [ObservableProperty]
        private int _z;

        /// <summary>
        /// 快捷获取/设置指定轴的位置值
        /// </summary>
        public int this[AxisType axis]
        {
            get => axis switch
            {
                AxisType.X => X,
                AxisType.Y => Y,
                AxisType.Z => Z,
                _ => 0
            };
            set
            {
                switch (axis)
                {
                    case AxisType.X: X = value; break;
                    case AxisType.Y: Y = value; break;
                    case AxisType.Z: Z = value; break;
                }
            }
        }
    }

    /// <summary>
    /// 三维平台运行状态模型。
    /// 集中管理平台的运动状态、复位状态以及各轴的物理边界触发标志。
    /// </summary>
    public  partial class PlatformStatus : ObservableObject
    {
        [ObservableProperty]
        private bool _isMoving; // 工具包会自动生成公开的 IsMoving 属性

        [ObservableProperty]
        private bool _isHomed;  // 工具包会自动生成公开的 IsHomed 属性

        // --- 必须加上这一行 ---
        public bool HasReceivedZZero { get; set; }
        // ---------------------

        public Dictionary<AxisType, bool> IsAtMin { get; set; } = new()
    {
        { AxisType.X, false }, { AxisType.Y, false }, { AxisType.Z, false }
    };

        public Dictionary<AxisType, bool> IsAtMax { get; set; } = new()
    {
        { AxisType.X, false }, { AxisType.Y, false }, { AxisType.Z, false }
    };
    }

    /// <summary>
    /// 三维平台物理限制常量
    /// 统一管理硬编码的软限位参数
    /// </summary>
    public static class PlatformLimits
    {
        // 软限位步进值（根据实际硬件行程定义）
        public const int MaxStepX = 15500;
        public const int MaxStepY = 7600;
        public const int MaxStepZ = 4600;

        // Z 轴特殊安全限制：防止探针撞击底盘
        public const int ZAxisMinLimit = -2000;

        // 默认有效步长范围
        public const int MinValidStep = 1;
        public const int MaxValidStep = 16500;
    }
}
