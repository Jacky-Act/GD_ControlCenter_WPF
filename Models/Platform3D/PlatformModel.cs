/*
 * 文件名: PlatformModel.cs
 * 描述: 本文件定义了三维运动平台的物理模型、运行状态以及软限位常量。
 * 架构规范: 纯数据模型 (POCO)，已剥离 MVVM 框架依赖。
 * 项目: GD_ControlCenter_WPF
 */

using System.Collections.Generic;

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
    /// 三维平台实时位置模型 (纯数据容器)
    /// 用于记录 X、Y、Z 三轴当前的脉冲步数。
    /// </summary>
    public class PlatformPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

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
    /// 三维平台运行状态模型 (纯数据容器)
    /// 集中管理平台的运动状态、复位状态以及各轴的物理边界触发标志。
    /// </summary>
    public class PlatformStatus
    {
        public bool IsMoving { get; set; }
        public bool IsHomed { get; set; }

        /// <summary>
        /// 各轴零点边界标志（Min）
        /// </summary>
        public Dictionary<AxisType, bool> IsAtMin { get; set; } = new()
        {
            { AxisType.X, false },
            { AxisType.Y, false },
            { AxisType.Z, false }
        };

        /// <summary>
        /// 各轴最大值边界标志（Max）
        /// </summary>
        public Dictionary<AxisType, bool> IsAtMax { get; set; } = new()
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

        // Z轴独有的负向软限位 (突破硬件 0 点),适配于左侧的机器
        public const int MinStepZ = -2000;

        // 默认有效步长范围
        public const int MinValidStep = 1;
        public const int MaxValidStep = 16500;
    }
}