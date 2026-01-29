using CommunityToolkit.Mvvm.ComponentModel;

namespace GD_ControlCenter_WPF.Models.Spectrometer
{
    /// <summary>
    /// 光谱仪配置模型
    /// 职责：存储设备基础信息与测量参数，支持 WPF 数据绑定与属性变更通知。
    /// </summary>
    public partial class SpectrometerConfig : ObservableObject
    {
        // --- 设备身份信息 ---

        /// <summary>
        /// 获取或设置设备唯一的序列号（SerialNumber）。
        /// </summary>
        [ObservableProperty]
        private string _serialNumber = string.Empty;

        /// <summary>
        /// 获取或设置设备友好名称（UserFriendlyName）。
        /// </summary>
        [ObservableProperty]
        private string _deviceName = string.Empty;

        /// <summary>
        /// 获取或设置 SDK 操作句柄（DeviceHandle）。
        /// 初始化成功后由 AVS_Activate 返回。
        /// </summary>
        [ObservableProperty]
        private int _deviceHandle = -1;

        /// <summary>
        /// 获取或设置设备当前的连接状态。
        /// </summary>
        [ObservableProperty]
        private bool _isConnected = false;

        // --- 测量参数配置 ---

        /// <summary>
        /// 获取或设置积分时间（Integration Time），单位为毫秒 (ms)。
        /// </summary>
        [ObservableProperty]
        private float _integrationTimeMs = 100f;

        /// <summary>
        /// 获取或设置硬件采样的平均次数（NrAverages）。
        /// </summary>
        [ObservableProperty]
        private uint _averagingCount = 5;

        /// <summary>
        /// 获取或设置起始像素索引。
        /// </summary>
        [ObservableProperty]
        private ushort _startPixel = 0;

        /// <summary>
        /// 获取或设置终止像素索引。
        /// </summary>
        [ObservableProperty]
        private ushort _stopPixel = 4095;

        /// <summary>
        /// 获取或设置是否启用饱和检测（Saturation Detection）。
        /// 开启后可监测像素是否过曝。
        /// </summary>
        [ObservableProperty]
        private bool _isSaturationDetectionEnabled = true;

        // --- 硬件限制 ---

        /// <summary>
        /// 获取或设置硬件支持的最小积分时间 (ms)。
        /// 用于 UI 输入值的合法性校验，防止下发 SDK 不支持的参数。
        /// </summary>
        [ObservableProperty]
        private float _minIntegrationTime = 5.0f; 
    }

    /// <summary>
    /// 光谱数据实体
    /// 职责：承载一次测量生成的物理数据。
    /// 仅包含纯粹的数据结构，以便在消息总线中高效传输。
    /// </summary>
    public class SpectralData
    {
        /// <summary>
        /// 获取或设置波长数组（单位：nm）。
        /// </summary>
        public double[] Wavelengths { get; set; } = Array.Empty<double>();

        /// <summary>
        /// 获取或设置光强/计数数组（单位：Counts）。
        /// </summary>
        public double[] Intensities { get; set; } = Array.Empty<double>();

        /// <summary>
        /// 获取或设置光谱数据的精确采集时间。
        /// </summary>
        public DateTime AcquisitionTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 获取或设置来源设备的序列号。
        /// 在多光谱仪拼接场景中，用于识别数据的物理来源。
        /// </summary>
        public string SourceDeviceSerial { get; set; } = string.Empty;

        /// <summary>
        /// 初始化 <see cref="SpectralData"/> 类的新实例。
        /// </summary>
        public SpectralData() { }

        /// <summary>
        /// 使用指定的波长、强度和序列号初始化 <see cref="SpectralData"/> 类。
        /// </summary>
        /// <param name="wavelengths">波长数组。</param>
        /// <param name="intensities">强度数组。</param>
        /// <param name="serial">来源设备序列号。</param>
        public SpectralData(double[] wavelengths, double[] intensities, string serial = "")
        {
            Wavelengths = wavelengths;
            Intensities = intensities;
            SourceDeviceSerial = serial;
        }
    }
}
