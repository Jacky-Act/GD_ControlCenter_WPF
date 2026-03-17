using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class TimeSeriesSamplingConfigViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;

        // --- 绑定的属性 ---

        [ObservableProperty]
        private ObservableCollection<SamplingPointItem> _samplingPoints = new();

        [ObservableProperty]
        private ObservableCollection<double> _availablePeaks = new(); // 预留接口：存放射线峰坐标

        [ObservableProperty]
        private string _newSampleName = string.Empty;

        public TimeSeriesSamplingConfigViewModel(JsonConfigService configService, Action closeAction)
        {
            _configService = configService;
            _closeAction = closeAction;

            LoadConfig();

            // 预留测试数据：等后面写了寻峰算法，直接将结果塞进这个列表即可
            // AvailablePeaks = new ObservableCollection<double> { 253.65, 313.15, 435.84, 546.07 };
        }

        private void LoadConfig()
        {
            var config = _configService.Load();
            if (config.TimeSeriesSampleNames != null)
            {
                // 将 AppConfig 中的纯字符串名称，包装成带有 X 坐标状态的 UI 实体
                foreach (var name in config.TimeSeriesSampleNames)
                {
                    SamplingPoints.Add(new SamplingPointItem(name));
                }
            }
        }

        // --- 交互命令 ---

        [RelayCommand]
        private void AddPoint()
        {
            if (string.IsNullOrWhiteSpace(NewSampleName)) return;

            string nameToAdd = NewSampleName.Trim();

            // 防呆：防止添加同名通道
            if (SamplingPoints.Any(p => p.Name == nameToAdd)) return;

            SamplingPoints.Add(new SamplingPointItem(nameToAdd));
            NewSampleName = string.Empty; // 添加成功后清空输入框
        }

        [RelayCommand]
        private void DeletePoint(SamplingPointItem item)
        {
            if (item != null && SamplingPoints.Contains(item))
            {
                SamplingPoints.Remove(item);
            }
        }

        [RelayCommand]
        private void Apply()
        {
            var config = _configService.Load();

            // 提取所有的名字存入本地配置（丢弃掉临时的特征峰配置）
            config.TimeSeriesSampleNames = SamplingPoints.Select(p => p.Name).ToList();
            _configService.Save(config);

            // TODO 预留：通过 WeakReferenceMessenger 发送一条消息，
            // 把携带了 SelectedPeakX 的完整 SamplingPoints 列表发给主界面的图表，让图表开始依据这些横坐标提取数据。

            _closeAction?.Invoke();
        }
    }

    /// <summary>
    /// 专用于时序图采样配置 UI 绑定的数据实体
    /// </summary>
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
}
