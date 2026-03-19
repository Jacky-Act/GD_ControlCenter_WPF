using C_Sharp_Application;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Data;

/*
 * 文件名: SpectrometerManager.cs
 * 模块: 硬件调度中心 (Hardware Orchestration Layer)
 * 描述: 光谱仪全局管理器 (单例)。
 * 架构职责: 
 * 1. 【向下管理硬件】：负责底层 SDK 的全局初始化、USB 设备的扫描与上下线同步。
 * 2. 【向上输送弹药】：作为所有设备的数据汇总漏斗，执行过曝拦截与多机数据拼接，最后统一通过总线广播。
 */

namespace GD_ControlCenter_WPF.Services.Spectrometer
{
    public class SpectrometerManager : IDisposable
    {
        #region 1. 单例与并发基建

        /// <summary>
        /// 获取管理器的全局唯一实例。
        /// </summary>
        public static SpectrometerManager Instance { get; } = new SpectrometerManager();

        /// <summary>
        /// 用于多线程环境下同步 ObservableCollection 的锁对象。
        /// </summary>
        private readonly object _devicesLock = new object();

        /// <summary>
        /// 并发字典：暂存多台设备最近一帧的光谱数据，Key 为设备序列号。
        /// 核心作用：作为“多机协作拼接”场景下的数据蓄水池。
        /// </summary>
        private readonly ConcurrentDictionary<string, SpectralData> _latestDataCache = new();

        /// <summary>
        /// 标识底层 SDK 是否已经完成了 AVS_Init 初始化操作。
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// 存储当前系统中所有已识别且初始化的光谱仪服务实例。
        /// 使用 ObservableCollection 以支持 UI 层 (如设备列表) 的直接双向绑定。
        /// </summary>
        public ObservableCollection<ISpectrometerService> Devices { get; } = new();

        /// <summary>
        /// 私有构造函数，强制单例模式。
        /// </summary>
        private SpectrometerManager()
        {
            // 【跨线程 UI 保护】：允许在后台线程安全地向 Devices 集合添加或移除元素，防止引发 UI 线程的异常崩溃
            BindingOperations.EnableCollectionSynchronization(Devices, _devicesLock);
        }

        #endregion

        #region 2. 生命周期与总线控制 (Initialize & Connect)

        /// <summary>
        /// 异步执行全局初始化，并扫描系统上的 USB/网络 光谱仪设备。
        /// </summary>
        /// <returns>返回成功发现并接入管理的设备总数，失败或未找到则返回 0 或 -1。</returns>
        public async Task<int> DiscoverAndInitDevicesAsync()
        {
            return await Task.Run(() =>
            {
                // 1. 全局 SDK 激活
                if (!_isInitialized)
                {
                    int initResult = AvantesSdk.AVS_Init(0);
                    if (initResult < 0) return -1;
                    _isInitialized = true;
                }

                // 2. 唤醒系统 USB 总线扫描
                AvantesSdk.AVS_UpdateUSBDevices();

                // 3. 两次探测法获取设备列表大小
                uint listSize = 0;
                uint requiredSize = 0;
                AvantesSdk.AVS_GetList(listSize, ref requiredSize, null!);

                if (requiredSize == 0) return 0;

                // 4. 正式抓取设备身份信息
                var deviceList = new AvantesSdk.AvsIdentityType[requiredSize / (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(AvantesSdk.AvsIdentityType))];
                AvantesSdk.AVS_GetList(requiredSize, ref requiredSize, deviceList);

                // 5. 将底层结构体同步为 C# 面向对象服务
                SyncDevices(deviceList);

                return Devices.Count;
            });
        }

        /// <summary>
        /// 一键启动：统一下发启动指令给所有在线设备。
        /// </summary>
        public void StartAll()
        {
            foreach (var device in Devices)
            {
                device.StartContinuousMeasurement();
                // 物理间隔缓冲，防止 USB 拥堵导致指令丢失
                System.Threading.Thread.Sleep(50);
            }
        }

        /// <summary>
        /// 一键停止：统一下发停止指令给所有在线设备。
        /// </summary>
        public void StopAll()
        {
            foreach (var device in Devices)
            {
                device.StopMeasurement();
                // 给底层驱动留出释放句柄的缓冲时间
                System.Threading.Thread.Sleep(100);
            }
        }

        /// <summary>
        /// 物理断开所有已连接的光谱仪硬件，并彻底释放底层 USB 资源。
        /// 通常在软件关闭或用户要求强行重连时调用。
        /// </summary>
        public void DisconnectAll()
        {
            StopAll();

            // 逐一释放每个设备的底层 USB 句柄，并注销数据监听
            foreach (var device in Devices)
            {
                device.DataReady -= OnDeviceDataReady;
                device.Dispose();
            }

            // 彻底释放 SDK 占用的整个 USB 总线资源
            if (_isInitialized)
            {
                AvantesSdk.AVS_Done();
                _isInitialized = false;
            }

            Devices.Clear();
            _latestDataCache.Clear();
        }

        public void Dispose()
        {
            DisconnectAll();
        }

        #endregion

        #region 3. 设备同步与事件挂载

        /// <summary>
        /// 内部方法：将 SDK 扫描到的设备身份数组转化为服务实例，并挂载中央数据监听。
        /// </summary>
        private void SyncDevices(AvantesSdk.AvsIdentityType[] newIdentities)
        {
            foreach (var identity in newIdentities)
            {
                // 已存在的设备跳过
                if (Devices.Any(d => d.Config.SerialNumber == identity.m_SerialNumber))
                    continue;

                var config = new SpectrometerConfig
                {
                    SerialNumber = identity.m_SerialNumber,
                    DeviceName = identity.m_UserFriendlyName
                };

                var service = new SpectrometerService(config);

                // 【核心流转】：所有单机设备采到的数据，全部通过这个事件流向 Manager 的 OnDeviceDataReady
                service.DataReady += OnDeviceDataReady;

                Devices.Add(service);
            }
        }

        #endregion

        #region 4. 【核心路由】数据总线分发引擎 (Data Routing Engine)

        /// <summary>
        /// 核心事件响应：这是所有底层数据浮出水面的第一站。
        /// 【重构说明】：采用流水线 (Pipeline) 模式，将过曝检测与数据分发解耦，主流程像散文一样清晰。
        /// </summary>
        /// <param name="data">任意一台设备回传的单次完整光谱数据。</param>
        private void OnDeviceDataReady(SpectralData data)
        {
            // 流水线 1：执行饱和度（过曝）安全检查
            HandleSaturationWarning(data);

            // 流水线 2：执行数据分发路由（判断是单机直传，还是多机拼接）
            HandleDataDispatching(data);
        }

        /// <summary>
        /// 路由策略 A：饱和度拦截报警
        /// </summary>
        private void HandleSaturationWarning(SpectralData data)
        {
            var sourceDevice = Devices.FirstOrDefault(d => d.Config.SerialNumber == data.SourceDeviceSerial);

            // 如果该设备开启了饱和检测，且算法确认出现过曝像素
            if (sourceDevice != null && sourceDevice.Config.IsSaturationDetectionEnabled)
            {
                if (SpectrometerLogic.CheckSaturation(data))
                {
                    // 向 UI 广播警告消息
                    WeakReferenceMessenger.Default.Send(new SpectrometerStatusMessage(sourceDevice.Config));
                }
            }
        }

        /// <summary>
        /// 路由策略 B：单机透传 / 多机拼接分流器
        /// </summary>
        private void HandleDataDispatching(SpectralData data)
        {
            int deviceCount = Devices.Count;

            if (deviceCount <= 1)
            {
                DispatchSingleDeviceData(data);
            }
            else
            {
                DispatchMultiDeviceStitchedData(data, deviceCount);
            }
        }

        /// <summary>
        /// 路由执行 B-1：单机模式直传
        /// </summary>
        private void DispatchSingleDeviceData(SpectralData data)
        {
            var sourceConfig = Devices[0].Config;

            // 直接包装为带硬件上下文的消息，发射给全局总线
            WeakReferenceMessenger.Default.Send(new SpectralDataMessage(data)
            {
                IntegrationTime = sourceConfig.IntegrationTimeMs,
                AveragingCount = sourceConfig.AveragingCount
            });
        }

        /// <summary>
        /// 路由执行 B-2：联机模式并发拼接
        /// </summary>
        private void DispatchMultiDeviceStitchedData(SpectralData data, int totalOnlineDevices)
        {
            // 放入并发蓄水池
            _latestDataCache[data.SourceDeviceSerial] = data;

            // 【屏障同步策略】：若蓄水池里的数据条数 >= 在线设备总数，说明各台机器都在这一个周期内交卷了
            if (_latestDataCache.Count >= totalOnlineDevices)
            {
                // 调用静态数学库执行“零内存分配”去重拼接
                var combinedData = SpectrometerLogic.PerformStitching(_latestDataCache.Values);

                if (combinedData != null)
                {
                    // 硬件参数以第一台主设备为准进行广播
                    var mainConfig = Devices[0].Config;
                    WeakReferenceMessenger.Default.Send(new SpectralDataMessage(combinedData)
                    {
                        IntegrationTime = mainConfig.IntegrationTimeMs,
                        AveragingCount = mainConfig.AveragingCount
                    });
                }

                // 泄洪清空蓄水池，准备迎接下一个并发同步周期
                _latestDataCache.Clear();
            }
        }

        #endregion
    }
}