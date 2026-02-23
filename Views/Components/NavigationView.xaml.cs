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
                if (MainTabControl.SelectedIndex == 0)
                {
                    vm.CurrentPage = vm.ControlPanelVM;
                }
                else if (MainTabControl.SelectedIndex == 1)
                {
                    vm.CurrentPage = vm.TimeSeriesVM;
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