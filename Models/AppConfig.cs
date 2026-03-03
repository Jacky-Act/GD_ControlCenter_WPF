using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.Models
{
    public class AppConfig
    {
        // --- 高压电源设置参数 ---
        public int LastHvVoltage { get; set; } = 0;
        public int LastHvCurrent { get; set; } = 0;

        // --- 蠕动泵参数 ---
        public int LastPumpSpeed { get; set; } = 0;

        // 方向：true 为顺时针/前向 (1)，false 为逆时针/后向 (0)
        public bool IsPumpClockwise { get; set; } = true;

        // --- 注射泵参数 ---
        // 移动距离范围 0-3000
        public int LastSyringeDistance { get; set; } = 0;

        // 移动方向：true 为输出 (1)，false 为输入 (0)
        public bool IsSyringeOutput { get; set; } = true;

        // 可以在此继续添加其他设备的参数，例如：
        // public int LastPumpRpm { get; set; } = 100;
    }
}
