using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages.GD_ControlCenter_WPF.Models.Messages;
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

        public SettingsViewModel(ISerialPortService serialService, JsonConfigService configService)
        {
            _serialService = serialService;
            _configService = configService;

            _serialService.ConnectionStatusChanged += OnConnectionStatusChanged;

            // 软件启动时，加载上一次保存的状态
            var config = _configService.Load();
            IsSpectrometerEnabled = config.IsSpectrometerEnabled;
            IsAutoReigniteEnabled = config.IsAutoReigniteEnabled;

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
    }
}