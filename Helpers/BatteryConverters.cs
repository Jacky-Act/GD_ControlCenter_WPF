using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.Helpers
{
    // 1. 计算电量槽宽度的多绑定转换器 (保持不变)
    public class BatteryWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is int percentage && values[1] is double actualWidth)
            {
                double maxWidth = actualWidth - 4; // 预留边距
                return Math.Max(0, maxWidth * (percentage / 100.0));
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // 2. 修改：根据电量百分比返回颜色的转换器 (已简化)
    public class BatteryStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int percentage)
            {
                // 逻辑：低于20%红值，其余绿值
                if (percentage < 20) return Brushes.IndianRed;
                return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // 绿色
            }
            return Brushes.Gray; // 默认/异常色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // 3. 通用布尔转可见性 (用于充电闪电图标)
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = value is bool b && b;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // 4. 反向布尔转可见性 (用于断开插头图标 PowerPlugOff)
    public class InverseBoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 当 IsOnline 为 False 时显示图标
            bool isVisible = value is bool b && !b;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}