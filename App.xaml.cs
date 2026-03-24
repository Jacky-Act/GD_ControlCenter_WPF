using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.ViewModels;
using GD_ControlCenter_WPF.Services.Platform3D; 
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace GD_ControlCenter_WPF
{
    public partial class App : Application
    {
        // 全局服务提供者
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();

            // 1. 注册基础服务 (单例，全局唯一)
            services.AddSingleton<JsonConfigService>();
            services.AddSingleton<ISerialPortService, SerialPortService>(); // 接口绑定实现
            services.AddSingleton<ProtocolService>();

            // 2. 注册硬件服务 (单例)
            services.AddSingleton<GeneralDeviceService>();
            services.AddSingleton<BatteryService>();
            services.AddSingleton<HighVoltageService>();
            services.AddSingleton<PeakTrackingService>();
            services.AddSingleton<IPlatform3DService, Platform3DService>();
            services.AddSingleton<PlatformCalibrationService>();

            // 3. 注册 ViewModels (单例或瞬态)
            services.AddSingleton<BatteryViewModel>();
            services.AddSingleton<HighVoltageViewModel>();
            services.AddSingleton<ControlPanelViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<PeristalticPumpViewModel>();
            services.AddSingleton<SyringePumpViewModel>();
            services.AddSingleton<TimeSeriesViewModel>();

            // 构建容器
            Services = services.BuildServiceProvider();

            // 统一前置初始化逻辑（如需设置历史高压参数）
            var config = Services.GetRequiredService<JsonConfigService>().Load();
            var hvService = Services.GetRequiredService<HighVoltageService>();
            hvService.SetHighVoltage(config.LastHvVoltage, config.LastHvCurrent);
        }

        /// <summary>
        /// 应用程序退出时的生命周期钩子
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // 1. 触发记录模块的紧急静默保存 
                var timeSeriesVM = Services.GetRequiredService<TimeSeriesViewModel>();
                timeSeriesVM.EmergencySave();

                var controlPanelVM = Services.GetRequiredService<ControlPanelViewModel>();
                controlPanelVM.EmergencySave();

                var hvService = Services.GetRequiredService<HighVoltageService>();
                var generalService = Services.GetRequiredService<GeneralDeviceService>();
                var serialService = Services.GetRequiredService<ISerialPortService>();

                // 2. 下发硬件关闭指令        
                generalService.ControlPeristalticPump(0, true, false);
                Thread.Sleep(300);
                hvService.SetHighVoltage(0, 0);
                Thread.Sleep(300);

                // 3. 将极其容易引发死锁的断开操作，丢入独立任务
                var cleanupTask = Task.Run(() =>
                {
                    try { SpectrometerManager.Instance.StopAll(); } catch { }
                    try { serialService.Close(); } catch { }
                });

                // 给底层驱动1.5 秒的释放，时间一到不管有没有弄完，直接强制放行。
                cleanupTask.Wait(1500);
            }
            catch (Exception)
            {
            }
            finally
            {
                base.OnExit(e);

                // 调用系统底层的 Kill() 指令，从 Windows 任务管理器级别直接将当前进程“斩首”。
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
        }
    }
}