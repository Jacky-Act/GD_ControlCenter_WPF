using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GD_ControlCenter_WPF.Views.Pages
{
    /// <summary>
    /// ControlPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class ControlPanelView : UserControl
    {
        public ControlPanelView()
        {
            InitializeComponent();

            // 初始化时添加波形图层
            double[] dataX = { 1, 2, 3, 4, 5 };
            double[] dataY = { 1, 4, 9, 16, 25 };

            // 使用 Add.Scatter (散点/折线) 或 Add.Signal (高性能信号源)
            SpecPlot.Plot.Add.Scatter(dataX, dataY);

            // 刷新显示
            SpecPlot.Refresh();
        }
    }
}
