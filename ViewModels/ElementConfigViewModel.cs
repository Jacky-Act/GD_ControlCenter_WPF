using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class ElementConfigViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;

        // 1. 最左侧的“检测元素”列表与选中项联动逻辑
        [ObservableProperty] private ObservableCollection<ElementItemModel> _elements = new();
        private ElementItemModel? _selectedElement;
        public ElementItemModel? SelectedElement
        {
            get => _selectedElement;
            set
            {
                if (SetProperty(ref _selectedElement, value))
                    Wavelengths = value != null ? value.Wavelengths : new ObservableCollection<WavelengthItemModel>();
            }
        }

        // 2. 中间的“目标波长”列表
        [ObservableProperty] private ObservableCollection<WavelengthItemModel> _wavelengths = new(); [ObservableProperty] private WavelengthItemModel? _selectedWavelength;

        // 3. 右侧的“已选分析配置”列表
        [ObservableProperty] private ObservableCollection<AnalysisConfigItem> _selectedConfigs = new();
        [ObservableProperty] private AnalysisConfigItem? _currentSelectedConfig;

        // ================= 仪表台硬件参数同步展示 =================
        [ObservableProperty] private string _globalCurrent = "-";
        [ObservableProperty] private string _globalPumpSpeed = "-";
        [ObservableProperty] private string _globalSampleCount = "-";
        [ObservableProperty] private string _globalSampleInterval = "-";

        public ElementConfigViewModel(JsonConfigService configService)
        {
            _configService = configService;
            RefreshGlobalSettings(); // 初始化时读取一次
        }

        /// <summary>
        /// 从全局 JSON 配置中读取仪表台最新设置的参数
        /// </summary>
        public void RefreshGlobalSettings()
        {
            var config = _configService.Load();
            GlobalCurrent = config.LastHvCurrent.ToString();
            GlobalPumpSpeed = config.LastPumpSpeed.ToString();
            GlobalSampleCount = config.LastSampleCount.ToString();
            GlobalSampleInterval = config.LastSampleInterval.ToString();
        }

        // ================= 按钮交互命令 =================

        [RelayCommand]
        private void AddElement()
        {
            var window = new AddElementWindow();
            var vm = new AddElementViewModel((name) =>
            {
                if (!Elements.Any(e => e.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)))
                {
                    var newElement = new ElementItemModel(name);
                    Elements.Add(newElement);
                    SelectedElement = newElement;
                }
            }, () => window.Close());
            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        [RelayCommand]
        private void AddWavelength()
        {
            if (SelectedElement == null) return;
            var window = new AddWavelengthWindow();
            var vm = new AddWavelengthViewModel((val) =>
            {
                if (!SelectedElement.Wavelengths.Any(w => w.Value == val))
                {
                    var newWl = new WavelengthItemModel(val);
                    SelectedElement.Wavelengths.Add(newWl);
                    SelectedWavelength = newWl;
                }
            }, () => window.Close());
            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        [RelayCommand]
        private void SelectCurrentConfig()
        {
            if (SelectedElement == null || SelectedWavelength == null) return;

            // 加入前，强制刷新一次获取最新的仪表台数据
            RefreshGlobalSettings();

            if (SelectedConfigs.Any(x => x.ElementName == SelectedElement.Name && x.Wavelength == SelectedWavelength.Value)) return;

            var newItem = new AnalysisConfigItem
            {
                ElementName = SelectedElement.Name,
                Wavelength = SelectedWavelength.Value,

                // 绑定最新获取到的硬件参数
                CurrentConfigText = GlobalCurrent,
                SpeedConfigText = GlobalPumpSpeed,
                SampleCountText = GlobalSampleCount,
                SampleIntervalText = GlobalSampleInterval
            };

            SelectedConfigs.Add(newItem);
            WeakReferenceMessenger.Default.Send(new ActiveConfigsChangedMessage(SelectedConfigs.ToList()));
        }

        [RelayCommand]
        private void RemoveConfig()
        {
            if (CurrentSelectedConfig != null)
            {
                SelectedConfigs.Remove(CurrentSelectedConfig);
                WeakReferenceMessenger.Default.Send(new ActiveConfigsChangedMessage(SelectedConfigs.ToList()));
            }
        }
    }
}