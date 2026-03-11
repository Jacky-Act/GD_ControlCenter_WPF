using C_Sharp_Application;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages; // 请核对你的实际命名空间
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    /// <summary>
    /// 光谱仪管理器 (全局单例)
    /// 职责：负责全局 SDK 初始化、设备发现、实例生命周期管理，以及多设备的数据汇总与分发。
    /// </summary>
    public class SpectrometerManager : IDisposable
    {
        // --- 私有只读字段 ---

        /// <summary>
        /// 用于多线程环境下同步 ObservableCollection 的锁对象。
        /// </summary>
        private readonly object _devicesLock = new object();

        /// <summary>
        /// 暂存多台设备最近的光谱数据，Key 为序列号。用于多机协作场景中的全谱拼接。
        /// </summary>
        private readonly ConcurrentDictionary<string, SpectralData> _latestDataCache = new();

        // --- 私有可变字段 ---

        /// <summary>
        /// 标识底层 SDK 是否已经完成了初始化。
        /// </summary>
        private bool _isInitialized = false;

        // --- 属性 ---

        /// <summary>
        /// 存储当前系统中所有已识别且初始化的光谱仪服务实例。
        /// 支持在 UI 层直接进行双向绑定。
        /// </summary>
        public ObservableCollection<ISpectrometerService> Devices { get; } = new();

        /// <summary>
        /// 获取管理器的全局唯一实例。
        /// </summary>
        public static SpectrometerManager Instance { get; } = new SpectrometerManager();

        // --- 构造函数 ---

        /// <summary>
        /// 私有构造函数，确保单例模式。配置集合的跨线程同步。
        /// </summary>
        private SpectrometerManager()
        {
            // 允许跨线程安全地更新 Devices 集合，防止 UI 线程崩溃
            BindingOperations.EnableCollectionSynchronization(Devices, _devicesLock);
        }

        // --- 公开方法 ---

        /// <summary>
        /// 异步执行全局初始化并扫描 USB/网络 设备。
        /// </summary>
        /// <returns>返回成功发现并接入系统管理的设备总数，失败或未找到则返回 0 或 -1。</returns>
        public async Task<int> DiscoverAndInitDevicesAsync()
        {
            return await Task.Run(() =>
            {
                if (!_isInitialized)
                {
                    int initResult = AvantesSdk.AVS_Init(0);
                    if (initResult < 0) return -1;
                    _isInitialized = true;
                }

                AvantesSdk.AVS_UpdateUSBDevices();
                uint listSize = 0;
                uint requiredSize = 0;
                AvantesSdk.AVS_GetList(listSize, ref requiredSize, null!);

                if (requiredSize == 0) return 0;

                var deviceList = new AvantesSdk.AvsIdentityType[requiredSize / (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(AvantesSdk.AvsIdentityType))];
                AvantesSdk.AVS_GetList(requiredSize, ref requiredSize, deviceList);

                SyncDevices(deviceList);

                return Devices.Count;
            });
        }

        /// <summary>
        /// 统一启动系统中所有在线设备的持续测量。
        /// </summary>
        public void StartAll()
        {
            foreach (var device in Devices)
            {
                device.StartContinuousMeasurement();
                System.Threading.Thread.Sleep(50); // 物理间隔，防止 USB 拥堵
            }
        }

        /// <summary>
        /// 统一停止系统中所有在线设备的测量动作。
        /// </summary>
        public void StopAll()
        {
            foreach (var device in Devices)
            {
                device.StopMeasurement();
                System.Threading.Thread.Sleep(100); // 给底层驱动留出释放句柄的时间
            }
        }

        /// <summary>
        /// 释放所有托管与非托管资源，安全断开硬件并清空缓存。
        /// </summary>
        public void Dispose()
        {
            StopAll();
            foreach (var device in Devices)
            {
                device.DataReady -= OnDeviceDataReady; // 解绑防泄漏
                device.Dispose();
            }
            if (_isInitialized)
            {
                AvantesSdk.AVS_Done();
            }
            Devices.Clear();
            _latestDataCache.Clear();
        }

        // --- 私有辅助方法 ---

        /// <summary>
        /// 将 SDK 扫描到的设备身份信息转化为内部管理的服务实例，并挂载数据监听。
        /// </summary>
        /// <param name="newIdentities">SDK 回传的设备身份数组。</param>
        private void SyncDevices(AvantesSdk.AvsIdentityType[] newIdentities)
        {
            foreach (var identity in newIdentities)
            {
                if (Devices.Any(d => d.Config.SerialNumber == identity.m_SerialNumber))
                    continue;

                var config = new SpectrometerConfig
                {
                    SerialNumber = identity.m_SerialNumber,
                    DeviceName = identity.m_UserFriendlyName
                };

                var service = new SpectrometerService(config);

                // 【核心流转】：直接在此处挂载底层数据的接收事件
                service.DataReady += OnDeviceDataReady;

                Devices.Add(service);
            }
        }

        /// <summary>
        /// 响应底层设备的数据就绪事件，执行饱和度校验及多机拼接拦截逻辑。
        /// </summary>
        /// <param name="data">任意一台设备回传的单次完整光谱数据。</param>
        private void OnDeviceDataReady(SpectralData data)
        {
            // 1. 查找数据来源设备的配置信息
            var sourceDevice = Devices.FirstOrDefault(d => d.Config.SerialNumber == data.SourceDeviceSerial);

            // 2. 饱和检查拦截 (调用静态工具类)
            if (sourceDevice != null && sourceDevice.Config.IsSaturationDetectionEnabled)
            {
                if (SpectrometerLogic.CheckSaturation(data))
                {
                    WeakReferenceMessenger.Default.Send(new SpectrometerStatusMessage(sourceDevice.Config));
                }
            }

            // 3. 路由策略分发
            int deviceCount = Devices.Count;

            if (deviceCount <= 1)
            {
                // 【单机模式】：直接透传给前端 UI
                WeakReferenceMessenger.Default.Send(new SpectralDataMessage(data));
            }
            else
            {
                // 【联机模式】：放入并发缓存池
                _latestDataCache[data.SourceDeviceSerial] = data;

                // 屏障同步策略：若缓存数据条数已达到在线设备总数，说明当前采集周期数据已凑齐
                if (_latestDataCache.Count >= deviceCount)
                {
                    // 调用静态算法库执行去重拼接
                    var combinedData = SpectrometerLogic.PerformStitching(_latestDataCache.Values);

                    if (combinedData != null)
                    {
                        WeakReferenceMessenger.Default.Send(new SpectralDataMessage(combinedData));
                    }

                    // 清空缓存，进入下一个并发等待周期
                    _latestDataCache.Clear();
                }
            }
        }
    }
}