using GD_ControlCenter_WPF.ViewModels;
using GD_ControlCenter_WPF.Services; // 引入 JsonConfigService 所在的命名空间
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace GD_ControlCenter_WPF.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // 从全局 IoC 容器中获取实例化好的 MainViewModel 并绑定
            this.DataContext = App.Services.GetRequiredService<MainViewModel>();

            // 1. 初始化尺寸与恢复状态
            ApplyScreenProportionalSize();

            // 2. 挂载关闭事件，拦截并保存尺寸
            this.Closing += MainWindow_Closing;
        }

        /// <summary>
        /// 按照屏幕比例初始化窗口最小尺寸，并读取配置恢复上次关闭时的尺寸与状态
        /// </summary>
        private void ApplyScreenProportionalSize()
        {
            // 获取 JsonConfigService 管家，并让它从硬盘读出最新的配置
            var configService = App.Services.GetRequiredService<JsonConfigService>();
            var config = configService.Load();

            // 1. 获取主屏幕工作区尺寸（排除任务栏）
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;

            // 2. 设定窗口占用屏幕的比例 (保底尺寸)
            double widthRatio = 0.80;
            double heightRatio = 0.72;

            // 3. 计算默认目标宽高值
            double targetWidth = screenWidth * widthRatio;
            double targetHeight = screenHeight * heightRatio;

            // 4. 设定最小尺寸限制 (防呆)
            this.MinWidth = targetWidth;
            this.MinHeight = targetHeight;

            // 5. 应用配置文件中的尺寸，使用 Math.Max 兜底
            this.Width = Math.Max(targetWidth, config.MainWindowWidth);
            this.Height = Math.Max(targetHeight, config.MainWindowHeight);

            // 6. 恢复最大化状态
            if (config.IsMainWindowMaximized)
            {
                this.WindowState = WindowState.Maximized;
            }

            // 7. 将计算出的“初始高度的 35%”传递给 MainViewModel
            if (this.DataContext is MainViewModel vm)
            {
                vm.DashboardHeight = targetHeight * 0.35;
                vm.ControlPanelVM.SetInitialHeight(targetHeight * 0.35);
            }
        }

        /// <summary>
        /// 窗口关闭时的处理逻辑：保存当前窗口的尺寸与状态到本地配置文件
        /// </summary>
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var configService = App.Services.GetRequiredService<JsonConfigService>();

            // 在关闭时，先从硬盘读取最新的配置
            var config = configService.Load();

            // 记录是否处于最大化状态
            config.IsMainWindowMaximized = (this.WindowState == WindowState.Maximized);

            // 记录恢复状态下的真实尺寸
            if (this.RestoreBounds.Width > 0 && this.RestoreBounds.Height > 0)
            {
                config.MainWindowWidth = this.RestoreBounds.Width;
                config.MainWindowHeight = this.RestoreBounds.Height;
            }

            // 将更新了尺寸信息的配置实体，交还给管家存入硬盘
            configService.Save(config);
        }
    }
}