using GD_ControlCenter_WPF.ViewModels;
using System.Windows;

namespace GD_ControlCenter_WPF.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ApplyScreenProportionalSize();
        }

        /// <summary>
        /// 按照屏幕比例初始化窗口尺寸，并计算卡片区固定高度 [cite: 60, 63]
        /// </summary>
        private void ApplyScreenProportionalSize()
        {
            // 1. 获取主屏幕工作区尺寸（排除任务栏）
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;

            // 2. 设定窗口占用屏幕的比例
            double widthRatio = 0.65;
            double heightRatio = 0.60;

            // 3. 计算目标宽高值
            double targetWidth = screenWidth * widthRatio;
            double targetHeight = screenHeight * heightRatio;

            // 4. 应用窗口尺寸与最小尺寸限制
            this.Width = targetWidth;
            this.Height = targetHeight;
            this.MinWidth = targetWidth;
            this.MinHeight = targetHeight;

            // 5. 将计算出的“初始高度的 40%”传递给 MainViewModel
            // 这样 ControlPanelView 就能绑定这个固定高度，防止过度拉伸
            if (this.DataContext is MainViewModel vm)
            {
                // 核心逻辑：锁定卡片区域高度为初始窗口高度的 40%
                vm.DashboardHeight = targetHeight * 0.35;
                // 确保子 VM 也拿到了这个初始高度
                vm.ControlPanelVM.SetInitialHeight(targetHeight * 0.35);
            }
        }

    }
}