using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GD_ControlCenter_WPF.Tests
{
    public partial class TestWindow : Window
    {
        public TestWindow()
        {
            InitializeComponent();
            ApplyScreenProportionalSize();
        }

        private void ApplyScreenProportionalSize()
        {
            // 获取主屏幕的工作区宽高（排除任务栏）
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;

            // 设定占用比例（例如：宽度占屏幕 80%，高度占屏幕 60%）
            double widthRatio = 0.65;
            double heightRatio = 0.60;

            double targetWidth = screenWidth * widthRatio;
            double targetHeight = screenHeight * heightRatio;

            // 按照你的要求：将最小尺寸与默认尺寸设为一致
            this.Width = targetWidth;
            this.Height = targetHeight;

            this.MinWidth = targetWidth;
            this.MinHeight = targetHeight;
        }
    }
}