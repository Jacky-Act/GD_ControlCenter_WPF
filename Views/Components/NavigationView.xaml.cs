using GD_ControlCenter_WPF.ViewModels;
using System.Windows.Controls;

namespace GD_ControlCenter_WPF.Views.Components
{
    /// <summary>
    /// NavigationView.xaml 的交互逻辑
    /// </summary>
    public partial class NavigationView : UserControl
    {
        public NavigationView()
        {
            InitializeComponent();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防死循环：如果是被代码手动清空选中状态的，直接跳过
            if (MainTabControl.SelectedIndex == -1) return;

            // 核心互斥逻辑：点上方时，清空下方的选中状态
            if (BottomTabControl != null)
            {
                BottomTabControl.SelectedIndex = -1;
            }

            // 只有当 DataContext 是 MainViewModel 时才执行切换逻辑
            if (this.DataContext is MainViewModel vm)
            {
                switch (MainTabControl.SelectedIndex)
                {
                    case 0:
                        vm.CurrentPage = vm.ControlPanelVM;
                        break;
                    case 1:
                        vm.CurrentPage = vm.TimeSeriesVM;
                        break;
                    case 2:
                        vm.CurrentPage = vm.ElementConfigVM;
                        break;
                    case 3:
                        vm.CurrentPage = vm.FittingCurveVM;
                        break;
                    case 4:
                        vm.CurrentPage = vm.SampleMeasurementVM;
                        break;
                }
            }
        }

        /// <summary>
        /// 下方设置/帮助菜单切换事件
        /// </summary>
        private void BottomTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防死循环：如果是被代码手动清空选中状态的，直接跳过
            if (BottomTabControl.SelectedIndex == -1) return;

            // 核心互斥逻辑：点下方时，清空上方的选中状态
            if (MainTabControl != null)
            {
                MainTabControl.SelectedIndex = -1;
            }

            // 执行页面切换逻辑
            if (this.DataContext is MainViewModel vm)
            {
                if (BottomTabControl.SelectedItem == SettingsTab)
                {
                    vm.CurrentPage = vm.SettingsVM; // 切换到设置
                }
                else if (BottomTabControl.SelectedItem == HelpTab)
                {
                    // vm.CurrentPage = vm.HelpVM; // 切换到帮助（根据你实际代码调整）
                }
            }
        }
    }
}