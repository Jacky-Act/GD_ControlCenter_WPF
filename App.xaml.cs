using GD_ControlCenter_WPF.Services;
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
    }
}