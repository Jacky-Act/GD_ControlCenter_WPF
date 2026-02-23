using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private object _currentPage; // 当前显示的页面对象

        [ObservableProperty]
        private double _dashboardHeight; // 用于绑定到界面的卡片区高度

        public ControlPanelViewModel ControlPanelVM { get; } = new();
        public TimeSeriesViewModel TimeSeriesVM { get; } = new();

        public MainViewModel()
        {
            // 默认显示仪表台
            CurrentPage = ControlPanelVM;
        }
    }
}
