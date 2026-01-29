using C_Sharp_Application;
using GD_ControlCenter_WPF.Models.Spectrometer;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    /// <summary>
    /// 光谱仪管理器
    /// 职责：负责全局 SDK 初始化、设备发现、实例生命周期管理及多机协作。
    /// </summary>
    public class SpectrometerManager : IDisposable
    {
        private bool _isInitialized = false;
        private readonly object _devicesLock = new object();

        /// <summary>
        /// 存储当前系统中所有已识别的光谱仪服务实例
        /// </summary>
        public ObservableCollection<ISpectrometerService> Devices { get; } = new();

        // 将SpectrometerManager 注册为单例（静态实例）
        public static SpectrometerManager Instance { get; } = new SpectrometerManager();

        // 将构造函数设为私有，防止外部 new 多个出来
        private SpectrometerManager()
        {
            // --- MVVM 核心配置 ---
            // 这行代码告诉 WPF：当非 UI 线程修改 Devices 时，请自动使用 _devicesLock 进行同步
            BindingOperations.EnableCollectionSynchronization(Devices, _devicesLock);
        }

        /// <summary>
        /// 全局初始化并扫描设备
        /// </summary>
        /// <returns>发现的设备数量</returns>
        public async Task<int> DiscoverAndInitDevicesAsync()
        {
            return await Task.Run(() =>
            {
                // 初始化 SDK 引擎 (0 代表扫描所有端口)
                if (!_isInitialized)
                {
                    int initResult = AvantesSdk.AVS_Init(0);
                    if (initResult < 0) return -1;
                    _isInitialized = true;
                }

                // 更新并获取设备列表
                AvantesSdk.AVS_UpdateUSBDevices();
                uint listSize = 0;
                uint requiredSize = 0;
                AvantesSdk.AVS_GetList(listSize, ref requiredSize, null!);

                if (requiredSize == 0) return 0;

                var deviceList = new AvantesSdk.AvsIdentityType[requiredSize / (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(AvantesSdk.AvsIdentityType))];
                AvantesSdk.AVS_GetList(requiredSize, ref requiredSize, deviceList);

                // 同步到 Devices 列表
                SyncDevices(deviceList);

                return Devices.Count;
            });
        }

        /// <summary>
        /// 将 SDK 扫描到的身份信息转换为可操作的服务实例
        /// </summary>
        private void SyncDevices(AvantesSdk.AvsIdentityType[] newIdentities)
        {
            foreach (var identity in newIdentities)
            {
                // 检查是否已经是管理中的设备
                if (Devices.Any(d => d.Config.SerialNumber == identity.m_SerialNumber))
                    continue;

                // 创建新的配置模型
                var config = new SpectrometerConfig
                {
                    SerialNumber = identity.m_SerialNumber,
                    DeviceName = identity.m_UserFriendlyName
                };

                // 实例化 Service 并加入集合
                var service = new SpectrometerService(config);
                Devices.Add(service);
            }
        }

        /// <summary>
        /// 统一启动所有设备的持续测量（用于全谱拼接场景）
        /// </summary>
        public void StartAll()
        {
            foreach (var device in Devices)
            {
                device.StartContinuousMeasurement();
            }
        }

        /// <summary>
        /// 统一停止所有设备
        /// </summary>
        public void StopAll()
        {
            foreach (var device in Devices)
            {
                device.StopMeasurement();
            }
        }

        public void Dispose()
        {
            StopAll();
            foreach (var device in Devices)
            {
                device.Dispose();
            }
            if (_isInitialized)
                AvantesSdk.AVS_Done();
            Devices.Clear();
        }
    }
}
