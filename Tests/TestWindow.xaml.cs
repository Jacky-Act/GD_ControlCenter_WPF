using C_Sharp_Application;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Windows;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.Tests
{
    /// <summary>
    /// TestWindow.xaml 的交互逻辑
    /// </summary>
    public partial class TestWindow : Window
    {
        private readonly SpectrometerManager _manager;
        private SpectrometerLogic? _activeLogic;

        public TestWindow()
        {
            InitializeComponent();

            // 1. 获取单例管理器
            // 直接引用那个唯一的静态实例
            _manager = SpectrometerManager.Instance;

            // 2. 绑定下拉框数据源
            cmbDevices.ItemsSource = _manager.Devices;

            // 3. 注册光谱数据消息，用于更新界面显示
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() => UpdatePeakDisplay(m.Value));
            });
        }

        // --- 1. 设备管理事件 ---

        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            txtCaptureStatus.Text = "正在扫描 USB 端口...";
            int count = await _manager.DiscoverAndInitDevicesAsync(); //
            txtCaptureStatus.Text = $"扫描完成，发现 {count} 台设备。";

            if (count > 0 && cmbDevices.SelectedIndex == -1)
                cmbDevices.SelectedIndex = 0;
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (cmbDevices.SelectedItem is SpectrometerService selectedService)
            {
                bool success = await selectedService.InitializeAsync(); //
                if (success)
                {
                    ledStatus.Fill = Brushes.Green;
                    _activeLogic = new SpectrometerLogic(selectedService); // 初始化该设备的逻辑处理器
                    txtCaptureStatus.Text = $"{selectedService.Config.SerialNumber} 已激活，波长映射就绪。";
                }
                else
                {
                    ledStatus.Fill = Brushes.Red;
                    MessageBox.Show("设备激活失败，请检查连接或驱动。");
                }
            }
        }

        // --- 2. 参数与采集事件 ---

        private async void btnApplyConfig_Click(object sender, RoutedEventArgs e)
        {
            if (cmbDevices.SelectedItem is SpectrometerService service)
            {
                // 1. 验证 UI 输入数据的有效性
                if (float.TryParse(txtIntegrationTime.Text, out float ms) &&
                    uint.TryParse(txtAverages.Text, out uint avg))
                {
                    txtCaptureStatus.Text = "正在请求同步硬件参数...";

                    // 2. 调用封装好的安全更新方法（内部已包含停止与重启逻辑）
                    int result = await service.UpdateConfigurationAsync(ms, avg);

                    // 3. 根据结果更新 UI 状态
                    if (result == AvantesSdk.ERR_SUCCESS)
                    {
                        txtCaptureStatus.Text = "参数已实时更新。";
                    }
                    else if (result == -999)
                    {
                        // 专门处理单次测量冲突的情况
                        txtCaptureStatus.Text = "更新失败：设备正忙于单次采样。";
                        MessageBox.Show("光谱仪正在进行单次长积分采样，请等待当前测量完成后再更改配置。",
                                        "设备忙",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                    }
                    else
                    {
                        txtCaptureStatus.Text = $"参数更新失败，错误码: {result}";
                    }
                }
                else
                {
                    txtCaptureStatus.Text = "输入参数格式错误。";
                }
            }
        }

        private async void btnMeasureOnce_Click(object sender, RoutedEventArgs e)
        {
            if (cmbDevices.SelectedItem is SpectrometerService service)
            {
                txtCaptureStatus.Text = "正在单次采样...";
                var data = await service.MeasureOnceAsync(); //
                if (data != null) UpdatePeakDisplay(data);
                txtCaptureStatus.Text = "单次采样完成。";
            }
        }

        private void btnContinuous_Click(object sender, RoutedEventArgs e)
        {
            if (cmbDevices.SelectedItem is SpectrometerService service)
            {
                if (btnContinuous.IsChecked == true)
                {
                    service.StartContinuousMeasurement(); //
                    txtCaptureStatus.Text = "持续测量中...";
                }
                else
                {
                    service.StopMeasurement(); //
                    txtCaptureStatus.Text = "测量已停止。";
                }
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            btnContinuous.IsChecked = false;
            if (cmbDevices.SelectedItem is SpectrometerService service)
            {
                service.StopMeasurement(); //
                txtCaptureStatus.Text = "强制停止。";
            }
        }

        // --- 3. 界面更新逻辑 ---

        private void UpdatePeakDisplay(SpectralData data)
        {
            // 【修改点】: 增加判空，防止在未连接时收到残留消息导致崩溃
            if (_activeLogic == null || data == null) return;

            // 利用 Logic 类计算峰值
            var (wavelength, intensity) = _activeLogic.FindPeak(data);

            // 【建议】: 使用 BeginInvoke 异步更新，防止高频刷新时 UI 出现微卡顿
            Dispatcher.BeginInvoke(new Action(() => {
                lblPeakWavelength.Text = wavelength.ToString("F2");
                lblPeakIntensity.Text = intensity.ToString("F0");

                // 增加一个简单的饱和提示逻辑（可选）
                if (intensity > 65000)
                    lblPeakIntensity.Foreground = Brushes.Red;
                else
                    lblPeakIntensity.Foreground = Brushes.DarkRed;
            }));
        }
    }
}
