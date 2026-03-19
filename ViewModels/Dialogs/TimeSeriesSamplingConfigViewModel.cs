using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class TimeSeriesSamplingConfigViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;
        private readonly PeakTrackingService _peakTrackingService;
        private readonly Action _closeAction;

        // --- 绑定的属性 ---

        [ObservableProperty]
        private ObservableCollection<SamplingPointItem> _samplingPoints = new();

        [ObservableProperty]
        private ObservableCollection<PeakOptionItem> _availablePeaks = new();

        [ObservableProperty]
        private string _comboBoxHintText = "等待寻峰...";

        [ObservableProperty]
        private string _newSampleName = string.Empty;


        public TimeSeriesSamplingConfigViewModel(
            JsonConfigService configService,
            PeakTrackingService peakTrackingService,
            Action closeAction)
        {
            _configService = configService;
            _peakTrackingService = peakTrackingService;
            _closeAction = closeAction;

            LoadAvailablePeaks();
            LoadConfig();
            UpdatePeakAvailability();
        }

        private void LoadAvailablePeaks()
        {
            AvailablePeaks.Clear();

            // 【新增】：在列表最前方插入“无”选项，其内部值为 null
            AvailablePeaks.Add(new PeakOptionItem(null));

            foreach (var peak in _peakTrackingService.TrackedPeaks)
            {
                AvailablePeaks.Add(new PeakOptionItem(Math.Round(peak.BaseWavelength, 2)));
            }

            // 因为始终有一个“无”选项，所以判断条件改为 > 1
            ComboBoxHintText = AvailablePeaks.Count > 1 ? "请选择关联特征峰" : "等待寻峰...";
        }

        private void LoadConfig()
        {
            var config = _configService.Load();

            if (config.TimeSeriesSampleNodes != null)
            {
                foreach (var node in config.TimeSeriesSampleNodes)
                {
                    var item = new SamplingPointItem(node.Name);

                    if (node.SelectedPeakX.HasValue)
                    {
                        double targetX = node.SelectedPeakX.Value;

                        // 【修改】：比对时必须先确保 p.Value.HasValue，跳过那个 null 的“无”选项
                        var matchedPeak = AvailablePeaks.FirstOrDefault(p => p.Value.HasValue && Math.Abs(p.Value.Value - targetX) < 0.01);

                        if (matchedPeak != null)
                        {
                            item.SelectedPeakX = matchedPeak.Value;
                        }
                    }

                    item.PropertyChanged += OnSamplingPointPropertyChanged;
                    SamplingPoints.Add(item);
                }
            }
        }

        private void OnSamplingPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SamplingPointItem.SelectedPeakX))
            {
                UpdatePeakAvailability();
            }
        }

        private void UpdatePeakAvailability()
        {
            var selectedPeaks = SamplingPoints
                .Where(p => p.SelectedPeakX.HasValue)
                .Select(p => p.SelectedPeakX!.Value)
                .ToList();

            foreach (var peakOption in AvailablePeaks)
            {
                // 【新增】：如果是“无”选项 (Value == null)，它永远可用，不参与互斥置灰
                if (!peakOption.Value.HasValue)
                {
                    peakOption.IsAvailable = true;
                    continue;
                }

                peakOption.IsAvailable = !selectedPeaks.Any(selected => Math.Abs(selected - peakOption.Value!.Value) < 0.01);
            }
        }

        // --- 交互命令 ---

        [RelayCommand]
        private void AddPoint()
        {
            if (string.IsNullOrWhiteSpace(NewSampleName)) return;

            string nameToAdd = NewSampleName.Trim();
            if (SamplingPoints.Any(p => p.Name == nameToAdd)) return;

            var newItem = new SamplingPointItem(nameToAdd);
            newItem.PropertyChanged += OnSamplingPointPropertyChanged;

            SamplingPoints.Add(newItem);
            NewSampleName = string.Empty;
        }

        [RelayCommand]
        private void DeletePoint(SamplingPointItem item)
        {
            if (item != null && SamplingPoints.Contains(item))
            {
                item.PropertyChanged -= OnSamplingPointPropertyChanged;
                SamplingPoints.Remove(item);
                UpdatePeakAvailability();
            }
        }

        [RelayCommand]
        private void Apply()
        {
            var config = _configService.Load();

            config.TimeSeriesSampleNodes = SamplingPoints.Select(p => new TimeSeriesSampleNode
            {
                Name = p.Name,
                SelectedPeakX = p.SelectedPeakX
            }).ToList();

            _configService.Save(config);

            _closeAction?.Invoke();
        }
    }

    public partial class SamplingPointItem : ObservableObject
    {
        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private double? _selectedPeakX;

        public SamplingPointItem(string name)
        {
            Name = name;
        }
    }

    public partial class PeakOptionItem : ObservableObject
    {
        // 【核心修改】：将 double 改为可空类型 double?，使其能够承载 null 代表的“无”
        [ObservableProperty]
        private double? _value;

        [ObservableProperty]
        private bool _isAvailable = true;

        // 【修改】：根据是否有值动态返回显示文本
        public string DisplayText => Value.HasValue ? $"{Value.Value:F2} nm" : "无 (不关联)";

        public PeakOptionItem(double? value)
        {
            Value = value;
        }
    }
}