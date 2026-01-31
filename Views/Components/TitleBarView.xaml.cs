using CommunityToolkit.Mvvm.Input;
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

namespace GD_ControlCenter_WPF.Views.Components
{
    /// <summary>
    /// TitleBarView.xaml 的交互逻辑
    /// </summary>
    public partial class TitleBarView : UserControl
    {
        // 使用 ICommand 接口定义属性
        public ICommand CloseCommand { get; }
        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }

        public TitleBarView()
        {
            InitializeComponent();

            // 1. 关闭窗口命令
            CloseCommand = new RelayCommand(() => Window.GetWindow(this)?.Close());

            // 2. 最小化窗口命令
            MinimizeCommand = new RelayCommand(() =>
            {
                var win = Window.GetWindow(this);
                if (win != null) win.WindowState = WindowState.Minimized;
            });

            // 3. 最大化/还原窗口命令
            MaximizeCommand = new RelayCommand(() =>
            {
                var win = Window.GetWindow(this);
                if (win != null)
                {
                    win.WindowState = win.WindowState == WindowState.Maximized
                                      ? WindowState.Normal
                                      : WindowState.Maximized;
                }
            });
        }
    }
}
