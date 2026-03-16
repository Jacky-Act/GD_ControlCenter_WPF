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

            // 3. 注册 ViewModels (单例或瞬态)
            services.AddSingleton<BatteryViewModel>();
            services.AddSingleton<HighVoltageViewModel>();
            services.AddSingleton<ControlPanelViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<PeristalticPumpViewModel>();
            services.AddSingleton<SyringePumpViewModel>();

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
                // 从 IoC 容器中获取需要的服务
                var hvService = Services.GetRequiredService<HighVoltageService>();
                var generalService = Services.GetRequiredService<GeneralDeviceService>();
                var serialService = Services.GetRequiredService<ISerialPortService>();

                // 下发硬件关闭指令               
                hvService.SetHighVoltage(0, 0); // 关闭高压电源 (电压 0，电流 0)              
                generalService.ControlPeristalticPump(0, true, false);  // 关闭蠕动泵 (转速 0，状态 false 即停止)
                SpectrometerManager.Instance.StopAll(); // 安全停止光谱仪数据采集，释放底层 C++ 句柄;此操作必须在彻底退出前执行，以防 SDK 挂起

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
    }
}