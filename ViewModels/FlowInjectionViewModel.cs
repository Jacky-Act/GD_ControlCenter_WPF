using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System.Collections.ObjectModel;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class FlowInjectionViewModel : ObservableObject
    {
        private readonly FlowInjectionLogic _logic;

        // --- UI 绑定的数据集合 ---

        // 顶部表：模拟的多元素分析配置
        [ObservableProperty]
        private ObservableCollection<dynamic> _activeConfigs = new();

        // 右上表：所有被捕获的峰值记录 (元素 + 次序)
        [ObservableProperty]
        private ObservableCollection<FlowInjectionResultData> _scanRecords = new();

        // 右上表：当前被选中的那一行记录
        private FlowInjectionResultData? _selectedScanRecord;
        public FlowInjectionResultData? SelectedScanRecord
        {
            get => _selectedScanRecord;
            set
            {
                if (SetProperty(ref _selectedScanRecord, value))
                {
                    // 联动更新右下角的详情表
                    UpdateSelectedScanResults();
                }
            }
        }

        // 右下表：选中记录的详情（背景、峰值、时间等）
        [ObservableProperty]
        private ObservableCollection<FlowInjectionResultData> _selectedScanResults = new();

        // --- 时序图相关的绑定 ---

        [ObservableProperty]
        private ObservableCollection<string> _availableElements = new();

        private string _selectedPlotElement = string.Empty;
        public string SelectedPlotElement
        {
            get => _selectedPlotElement;
            set
            {
                if (SetProperty(ref _selectedPlotElement, value))
                {
                    // 切换元素时，发送消息让 View 层清空并重绘该元素的历史曲线
                    WeakReferenceMessenger.Default.Send(new SwitchPlotElementMessage(value));
                }
            }
        }

        public FlowInjectionViewModel()
        {
            _logic = new FlowInjectionLogic();

            // 1. 初始化模拟的分析配置 (实际中可从 JsonConfigService 加载)
            ActiveConfigs.Add(new { ElementName = "Pb", Wavelength = "283.3", EquationText = "y=0.5x+1", RSquaredText = "0.998" });
            ActiveConfigs.Add(new { ElementName = "Cd", Wavelength = "228.8", EquationText = "y=1.2x+0.5", RSquaredText = "0.999" });

            AvailableElements.Add("Pb");
            AvailableElements.Add("Cd");
            SelectedPlotElement = "Pb";

            // 2. 监听寻峰算法算出的峰值结果
            WeakReferenceMessenger.Default.Register<FlowInjectionResultMessage>(this, (r, m) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // 添加到总记录表
                    ScanRecords.Add(m.Value);
                    // 自动选中最新的一条，方便用户直接看到详情
                    SelectedScanRecord = m.Value;
                });
            });
        }

        private void UpdateSelectedScanResults()
        {
            SelectedScanResults.Clear();
            if (SelectedScanRecord != null)
            {
                SelectedScanResults.Add(SelectedScanRecord);
            }
        }

        // --- 控制命令 ---

        [RelayCommand]
        private void CollectBackground()
        {
            // 在实际代码中，可抓取当前最近的一帧光谱给 Logic
            // 这里为了演示，我们假设直接通知 Logic 取下一帧作为背景
            // _logic.CollectBackground(currentSpectralData);
        }

        [RelayCommand]
        private void StartScanning()
        {
            // 提取目标元素和波长字典
            var targets = new Dictionary<string, double>
            {
                { "Pb", 283.3 },
                { "Cd", 228.8 }
            };

            // 告诉寻峰逻辑开始工作
            _logic.StartScanning(targets);

            // 让底层硬件开始狂奔采集
            SpectrometerManager.Instance.StartAll();
        }

        [RelayCommand]
        private void StopScanning()
        {
            _logic.StopScanning();
            SpectrometerManager.Instance.StopAll();
        }
    }

}