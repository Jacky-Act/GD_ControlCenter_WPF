using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISerialPortService _serialService;
        private const int BAUD_RATE = 9600;

        [ObservableProperty]
        private ObservableCollection<string> _availablePorts = new();

        [ObservableProperty]
        private string? _selectedPort;

        [ObservableProperty]
        private string _connectButtonText = "连接";

        [ObservableProperty]
        private string _statusText = "未连接";

        // 此处推荐依赖接口 ISerialPortService，以符合依赖倒置原则
        public SettingsViewModel(ISerialPortService serialService)
        {
            _serialService = serialService;

            // 订阅底层服务的标准事件
            _serialService.ConnectionStatusChanged += OnConnectionStatusChanged;

            RefreshPorts();
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
            if (_serialService.IsOpen)
            {
                _serialService.Close();
            }
            else
            {
                if (string.IsNullOrEmpty(SelectedPort)) return;

                if (!_serialService.Open(SelectedPort, BAUD_RATE))
                {
                    MessageBox.Show($"无法打开串口 {SelectedPort}，可能被占用。", "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            // 无论是 Open 失败还是正常执行，都强制刷新一次 UI 确保同步
            UpdateStatusUI();
        }

        private void UpdateStatusUI()
        {
            if (_serialService.IsOpen)
            {
                ConnectButtonText = "断开";
                StatusText = $"已连接 ({_serialService.CurrentPortName ?? "未知"})";
            }
            else
            {
                ConnectButtonText = "连接";
                StatusText = "未连接";
            }
        }
    }
}