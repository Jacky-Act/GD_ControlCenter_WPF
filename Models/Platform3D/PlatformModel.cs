using CommunityToolkit.Mvvm.ComponentModel;

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
    /// 三维平台位置模型
    /// 使用 ObservableProperty 实现属性变更通知，适配 WPF 数据绑定
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
    /// 三维平台运行状态模型
    /// 整合了原本散落在 Controller 和 Repository 中的状态标志位
    /// </summary>
    public partial class PlatformStatus : ObservableObject
    {
        [ObservableProperty]
        private bool _isMoving;

        [ObservableProperty]
        private bool _isHomed;

        /// <summary>
        /// 各轴零点边界标志（Min）
        /// 使用字典统一管理，消除冗余的 switch 判断逻辑
        /// </summary>
        public Dictionary<AxisType, bool> IsAtMin { get; } = new()
        {
            { AxisType.X, false },
            { AxisType.Y, false },
            { AxisType.Z, false }
        };

        /// <summary>
        /// 各轴最大值边界标志（Max）
        /// </summary>
        public Dictionary<AxisType, bool> IsAtMax { get; } = new()
        {
            { AxisType.X, false },
            { AxisType.Y, false },
            { AxisType.Z, false }
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
