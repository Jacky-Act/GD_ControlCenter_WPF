using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISerialPortService _serialService;
        private readonly JsonConfigService _configService;
        private const int BAUD_RATE = 9600;

        [ObservableProperty]
        private ObservableCollection<string> _availablePorts = new();

        [ObservableProperty]
        private string? _selectedPort;

        [ObservableProperty]
        private string _statusText = "未连接";

        [ObservableProperty]
        private bool _isSpectrometerEnabled;

        [ObservableProperty]
        private bool _isAutoReigniteEnabled;

        [ObservableProperty]
        private double _ignitionDelaySeconds;

        [ObservableProperty]
        private string _singleSaveExportPath = string.Empty;

        [ObservableProperty]
        private string _timeSeriesSaveDirectory;

        public SettingsViewModel(ISerialPortService serialService, JsonConfigService configService)
        {
            _serialService = serialService;
            _configService = configService;

            _serialService.ConnectionStatusChanged += OnConnectionStatusChanged;

            // 软件启动时，加载上一次保存的状态
            var config = _configService.Load();
            IsSpectrometerEnabled = config.IsSpectrometerEnabled;
            IsAutoReigniteEnabled = config.IsAutoReigniteEnabled;
            IgnitionDelaySeconds = config.IgnitionDelaySeconds;
            // 【统一风格】：如果没设置过单次保存路径，UI 上也默认显示桌面
            SingleSaveExportPath = string.IsNullOrWhiteSpace(config.SingleSaveExportPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : config.SingleSaveExportPath;

            // 时序图保存路径，为空则默认显示桌面
            TimeSeriesSaveDirectory = string.IsNullOrWhiteSpace(config.TimeSeriesSaveDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : config.TimeSeriesSaveDirectory;

            RefreshPorts();
        }

        // 拦截开关改变事件：当拨动光谱仪开关时触发
        partial void OnIsSpectrometerEnabledChanged(bool value)
        {
            var config = _configService.Load();
            config.IsSpectrometerEnabled = value;
            _configService.Save(config);

            if (value)
            {
                _ = SpectrometerManager.Instance.DiscoverAndInitDevicesAsync();
            }
            else
            {
                // 断开：【修改这里】将其丢入后台线程池执行，解放 UI 线程
                _ = Task.Run(() =>
                {
                    SpectrometerManager.Instance.DisconnectAll();
                });
            }

            // 构造特殊标识的 Config 用于全局消息传递
            var globalStatusConfig = new SpectrometerConfig
            {
                SerialNumber = "GLOBAL_SWITCH", // 特殊标识，用于区分单机报警
                IsConnected = value
            };

            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
                new SpectrometerStatusMessage(globalStatusConfig));
        }

        // 拦截开关改变事件：当拨动自动点火开关时触发
        partial void OnIsAutoReigniteEnabledChanged(bool value)
        {
            var config = _configService.Load();
            config.IsAutoReigniteEnabled = value;
            _configService.Save(config);
        }

        // 【新增】拦截用户输入或调整时的数据变更，自动保存并限制范围
        partial void OnIgnitionDelaySecondsChanged(double value)
        {
            // 安全限制：1.0 到 5.0 之间
            if (value < 1.0) IgnitionDelaySeconds = 1.0;
            else if (value > 5.0) IgnitionDelaySeconds = 5.0;
            else
            {
                var config = _configService.Load();
                config.IgnitionDelaySeconds = Math.Round(value, 1); // 保留1位小数
                _configService.Save(config);
            }
        }

        /// <summary>
        /// 处理底层抛出的连接状态变更
        /// </summary>
        private void OnConnectionStatusChanged(bool isOpen)
        {
            // 底层事件可能在其他线程触发，利用 Dispatcher 切换回 UI 线程更新属性
            Application.Current.Dispatcher.Invoke(UpdateStatusUI);
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            var ports = _serialService.GetAvailablePorts();
            AvailablePorts.Clear();
            foreach (var port in ports)
            {
                AvailablePorts.Add(port);
            }

            if (!_serialService.IsOpen)
            {
                _serialService.AutoConnect();
            }

            if (_serialService.IsOpen && _serialService.CurrentPortName != null && AvailablePorts.Contains(_serialService.CurrentPortName))
            {
                SelectedPort = _serialService.CurrentPortName;
            }
            else if (AvailablePorts.Count > 0)
            {
                SelectedPort = AvailablePorts[0];
            }
            else
            {
                SelectedPort = null;
            }

            UpdateStatusUI();
        }

        [RelayCommand]
        private void Connect()
        {
            if (string.IsNullOrEmpty(SelectedPort)) return;

            // 如果当前已经处于连接状态，且选中的正是当前串口，则什么都不做
            if (_serialService.IsOpen && _serialService.CurrentPortName == SelectedPort)
            {
                return;
            }

            // 如果当前连接着其他串口，先断开旧的
            if (_serialService.IsOpen)
            {
                _serialService.Close();
            }

            // 尝试打开新串口
            if (!_serialService.Open(SelectedPort, BAUD_RATE))
            {
                MessageBox.Show($"无法打开串口 {SelectedPort}，可能被占用。", "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            UpdateStatusUI();
        }

        private void UpdateStatusUI()
        {
            // 只负责更新右侧的状态提示文字
            if (_serialService.IsOpen)
            {
                StatusText = $"已连接 ({_serialService.CurrentPortName ?? "未知"})";
            }
            else
            {
                StatusText = "未连接";
            }
        }

        /// <summary>
        /// 打开文件夹选择对话框，并自动持久化保存新路径
        /// </summary>
        [RelayCommand]
        private void BrowseExportPath()
        {
            // 使用 Ookii 调出 Windows 10/11 风格的现代文件夹选择器
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
            {
                Description = "选择单次保存的默认文件夹",
                UseDescriptionForTitle = true,
                SelectedPath = SingleSaveExportPath
            };

            if (dialog.ShowDialog() == true)
            {
                SingleSaveExportPath = dialog.SelectedPath;

                // 立即持久化保存到本地 JSON
                var config = _configService.Load();
                config.SingleSaveExportPath = dialog.SelectedPath;
                _configService.Save(config);
            }
        }


        [RelayCommand]
        private void BrowseTimeSeriesDirectory()
        {
            // 复用 WinForms 文件夹选择器
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择时序图自动保存目录",
                UseDescriptionForTitle = true,
                SelectedPath = TimeSeriesSaveDirectory
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TimeSeriesSaveDirectory = dialog.SelectedPath;

                // 【说明】：更新到本地 Json 配置中 (根据您实际的保存逻辑调整)
                var config = _configService.Load();
                config.TimeSeriesSaveDirectory = TimeSeriesSaveDirectory;
                _configService.Save(config);
            }
        }

        // 【新增】给“上箭头”绑定的命令
        [RelayCommand]
        private void IncreaseIgnitionDelay()
        {
            IgnitionDelaySeconds = Math.Round(Math.Min(5.0, IgnitionDelaySeconds + 0.1), 1);
        }

        // 【新增】给“下箭头”绑定的命令
        [RelayCommand]
        private void DecreaseIgnitionDelay()
        {
            IgnitionDelaySeconds = Math.Round(Math.Max(1.0, IgnitionDelaySeconds - 0.1), 1);
        }
    }
}