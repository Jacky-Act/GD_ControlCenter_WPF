using System.Windows;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class HighVoltageViewModel : DeviceBaseViewModel
    {
        public HighVoltageViewModel()
        {
            DisplayName = "高压电源";
            ThemeColor = Brushes.DodgerBlue; // 蓝色主题
            DisplayValue = "0.0 V";
        }

        protected override void ExecuteOpenDetail()
        {
            // 此处后续编写弹出电源配置窗口的逻辑
            MessageBox.Show("打开高压电源控制详情");
        }
    }
}