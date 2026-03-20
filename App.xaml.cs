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
                // 【新增】：1. 触发两个记录模块的紧急静默保存 (同步阻塞，确保存完再关硬件)
                var timeSeriesVM = Services.GetRequiredService<TimeSeriesViewModel>();
                timeSeriesVM.EmergencySave();

                var controlPanelVM = Services.GetRequiredService<ControlPanelViewModel>();
                controlPanelVM.EmergencySave();

                // 从 IoC 容器中获取需要的服务
                var hvService = Services.GetRequiredService<HighVoltageService>();
                var generalService = Services.GetRequiredService<GeneralDeviceService>();
                var serialService = Services.GetRequiredService<ISerialPortService>();

                // 下发硬件关闭指令               
                generalService.ControlPeristalticPump(0, true, false);  // 关闭蠕动泵 (转速 0，状态 false 即停止)
                Thread.Sleep(500);
                hvService.SetHighVoltage(0, 0); // 关闭高压电源 (电压 0，电流 0)
                Thread.Sleep(500);

                SpectrometerManager.Instance.StopAll(); // 安全停止光谱仪数据采集

                // 等待异步队列清空，确保指令发送完毕
                Thread.Sleep(300);

                // 安全关闭串口
                serialService.Close();
            }
            catch (Exception)
            {
            }
            finally
            {
                base.OnExit(e);
            }
        }

        //protected override void OnExit(ExitEventArgs e)
        //{
        //    try
        //    {
        //        // 1. 先保存数据（这是最耗时的，放在最前）
        //        Services.GetRequiredService<TimeSeriesViewModel>().EmergencySave();
        //        Services.GetRequiredService<ControlPanelViewModel>().EmergencySave();

        //        var hvService = Services.GetRequiredService<HighVoltageService>();
        //        var generalService = Services.GetRequiredService<GeneralDeviceService>();
        //        var serialService = Services.GetRequiredService<ISerialPortService>();

        //        // 2. 依次下发指令，中间手动增加物理间隙
        //        // 关闭高压
        //        hvService.SetHighVoltage(0, 0);
        //        Thread.Sleep(100); // 关键：给串口缓冲留 100ms

        //        // 关闭蠕动泵（建议检查此方法内部是否是异步的，如果是，请调用它的同步版本或使用 .Wait()）
        //        generalService.ControlPeristalticPump(0, true, false);
        //        Thread.Sleep(150); // 蠕动泵可能协议较长，多留一点时间

        //        // 停止光谱仪
        //        SpectrometerManager.Instance.StopAll();
        //        Thread.Sleep(50);

        //        // 3. 确保指令全部发出后再关串口
        //        serialService.Close();
        //    }
        //    catch (Exception ex)
        //    {
        //        // 记录一下异常，方便调试
        //        System.Diagnostics.Debug.WriteLine($"退出异常: {ex.Message}");
        //    }
        //    finally
        //    {
        //        base.OnExit(e);
        //    }
        //}
    }
}