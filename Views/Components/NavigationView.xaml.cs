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
                        vm.CurrentPage = vm.LogVM;
                        break;
                    case 3:
                        vm.CurrentPage = vm.SpatialDistributionVM;
                        break;
                }
            }
        }

        private void HelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 不符合MVVM
        }

        private void SettingsButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 不符合MVVM
        }
    }
}