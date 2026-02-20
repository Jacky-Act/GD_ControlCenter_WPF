using System.Windows;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class PeristalticPumpViewModel : DeviceBaseViewModel
    {
        public PeristalticPumpViewModel()
        {
            DisplayName = "蠕动泵";
            ThemeColor = Brushes.SeaGreen; // 绿色主题
            DisplayValue = "0 RPM";
        }

        protected override void ExecuteOpenDetail()
        {
            // 此处后续编写弹出蠕动泵设置窗口的逻辑
            MessageBox.Show("打开蠕动泵控制详情");
        }
    }
}
