using System.Windows;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class SyringePumpViewModel : DeviceBaseViewModel
    {
        public SyringePumpViewModel()
        {
            DisplayName = "注射泵";
            ThemeColor = Brushes.DarkOrange; // 设置一个区别于蠕动泵的主题色
            DisplayValue = "0.00 mL/min";   // 初始值
        }

        protected override void ExecuteOpenDetail()
        {
            // 此处后续编写弹出注射泵参数配置窗口的逻辑
            MessageBox.Show("打开注射泵控制详情");
        }
    }
}