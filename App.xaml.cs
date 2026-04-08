using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.ViewModels;
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

            // ... 前面的代码不变

            // 3. 注册 ViewModels (单例或瞬态)
            services.AddSingleton<BatteryViewModel>();
            services.AddSingleton<HighVoltageViewModel>();
            services.AddSingleton<ControlPanelViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<PeristalticPumpViewModel>();
            services.AddSingleton<SyringePumpViewModel>();
            services.AddSingleton<TimeSeriesViewModel>();

            // ================= 核心修复：把所有 VM 纳入单例接管 =================
            services.AddSingleton<ElementConfigViewModel>();
            services.AddSingleton<SampleSequenceViewModel>();
            services.AddSingleton<SampleMeasurementViewModel>();
            services.AddSingleton<FlowInjectionViewModel>();
            services.AddSingleton<AnalysisWorkstationViewModel>();
            services.AddSingleton<MainViewModel>();
            // ====================================================================

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
        /// <summary>
        /// 应用程序退出时的生命周期钩子。
        /// 职责：执行数据紧急保存、下发硬件关闭指令、强制终止后台顽固进程。
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // 触发所有记录模块的紧急静默保存，防止内存数据丢失
                var timeSeriesVM = Services.GetRequiredService<TimeSeriesViewModel>();
                timeSeriesVM.EmergencySave();

                var controlPanelVM = Services.GetRequiredService<ControlPanelViewModel>();
                controlPanelVM.EmergencySave();

                // 按照安全顺序下发关闭指令
                var hvService = Services.GetRequiredService<HighVoltageService>();
                var generalService = Services.GetRequiredService<GeneralDeviceService>();
                var serialService = Services.GetRequiredService<ISerialPortService>();

                // 停止蠕动泵供液
                generalService.ControlPeristalticPump(0, true, false);
                Thread.Sleep(300); // 预留总线通讯时间

                // 关闭高压电源输出
                hvService.SetHighVoltage(0, 0);
                Thread.Sleep(300);

                // 将易引发死锁的断开操作（USB 句柄释放）丢入独立任务
                var cleanupTask = Task.Run(() =>
                {
                    try { SpectrometerManager.Instance.StopAll(); } catch { }
                    try { serialService.Close(); } catch { }
                });

                // 设置 1.5 秒硬超时。若底层驱动在此时间内未能响应，将不再等待直接进入强杀流程。
                cleanupTask.Wait(1500);
            }
            catch (Exception)
            {
                // 捕获所有退出异常，确保不弹出崩溃对话框
            }
            finally
            {
                base.OnExit(e);

                // 调用系统底层 Kill()，绕过所有托管与非托管的常规释放逻辑，直接从任务管理器级别销毁进程，防止“僵尸进程”残留。
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
        }
    }
}