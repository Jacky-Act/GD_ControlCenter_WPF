using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class SampleSequenceViewModel : ObservableObject
    {
        private readonly SequenceStorageService _storageService = new();
        private readonly ElementConfigViewModel _elementConfigVM; // 引入配置大管家
        private List<string> _activeElements = new();

        [ObservableProperty] private ObservableCollection<SampleItemModel> _samples = new();
        [ObservableProperty] private SampleItemModel? _selectedSample;

        [ObservableProperty] private ObservableCollection<string> _savedTemplates = new();
        [ObservableProperty] private string _selectedTemplate = string.Empty;
        [ObservableProperty] private string _newTemplateName = string.Empty;

        [ObservableProperty] private int _batchStandardCount = 3;
        [ObservableProperty] private int _batchUnknownCount = 10;

        public Array SampleTypes => Enum.GetValues(typeof(SampleType));
        public List<int> AvailableRepeats { get; } = Enumerable.Range(1, 99).ToList();

        // 构造函数注入 ElementConfigViewModel
        public SampleSequenceViewModel(ElementConfigViewModel elementConfigVM)
        {
            _elementConfigVM = elementConfigVM;
            RefreshTemplates();

            // 1. 页面一创建，立即主动拉取当前已选的元素名单（解决生命周期错过消息的问题）
            UpdateActiveElements(_elementConfigVM.SelectedConfigs.ToList());

            // 2. 依然保留被动监听，防止后续用户去配置页增删元素
            WeakReferenceMessenger.Default.Register<ActiveConfigsChangedMessage>(this, (r, m) => UpdateActiveElements(m.Value));

            // 3. 监听类型改变，触发智能重命名
            WeakReferenceMessenger.Default.Register<SampleTypeChangedMessage>(this, (r, m) => RenameSampleSmartly(m.Value));
        }

        /// <summary>
        /// 核心方法：更新元素名单，为所有样品补齐浓度格子，并通知 UI 重绘列
        /// </summary>
        private void UpdateActiveElements(List<AnalysisConfigItem> configs)
        {
            _activeElements = configs.Select(x => x.ElementName).Distinct().ToList();

            // 遍历现有所有样品，如果发现少了某个元素的坑位，立刻补上
            foreach (var sample in Samples)
            {
                foreach (var elName in _activeElements)
                {
                    if (!sample.ElementConcentrations.Any(c => c.ElementName == elName))
                    {
                        sample.ElementConcentrations.Add(new ElementConcentrationModel { ElementName = elName, ConcentrationValue = "" });
                    }
                }
            }

            // 发送重绘列的消息给 View 的后台代码
            WeakReferenceMessenger.Default.Send(new RebuildColumnsMessage(_activeElements));
        }

        private void RefreshTemplates()
        {
            SavedTemplates.Clear();
            foreach (var t in _storageService.GetSavedTemplates()) SavedTemplates.Add(t);
        }

        [RelayCommand]
        private void LoadTemplate()
        {
            if (string.IsNullOrEmpty(SelectedTemplate)) return;
            var data = _storageService.LoadTemplate(SelectedTemplate);
            if (data != null)
            {
                Samples.Clear();
                foreach (var item in data)
                {
                    Samples.Add(item);
                }
                // 加载完模板后，也要用当前的元素大名单筛一遍，补齐缺少的列
                UpdateActiveElements(_elementConfigVM.SelectedConfigs.ToList());
            }
        }

        [RelayCommand]
        private void SaveTemplate()
        {
            if (string.IsNullOrWhiteSpace(NewTemplateName)) return;
            _storageService.SaveTemplate(NewTemplateName, Samples.ToList());
            RefreshTemplates();
            SelectedTemplate = NewTemplateName;
            MessageBox.Show("序列模板保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void ExportCsv()
        {
            var dialog = new SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = "新建进样序列" };
            if (dialog.ShowDialog() == true)
            {
                _storageService.ExportToCsv(dialog.FileName, Samples.ToList());
                MessageBox.Show("导出 CSV 成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        private void BatchGenerate()
        {
            Samples.Clear();
            Samples.Add(CreateNewSample(SampleType.空白, "BLK-1"));
            for (int i = 1; i <= BatchStandardCount; i++) Samples.Add(CreateNewSample(SampleType.标液, $"STD-{i}"));
            for (int i = 1; i <= BatchUnknownCount; i++) Samples.Add(CreateNewSample(SampleType.待测液, $"待测液-{i}"));

            // 通知 UI 把列画出来
            WeakReferenceMessenger.Default.Send(new RebuildColumnsMessage(_activeElements));
        }

        private SampleItemModel CreateNewSample(SampleType type, string name)
        {
            var sample = new SampleItemModel { Type = type, SampleName = name };
            foreach (var el in _activeElements)
            {
                sample.ElementConcentrations.Add(new ElementConcentrationModel { ElementName = el, ConcentrationValue = "" });
            }
            return sample;
        }

        private void RenameSampleSmartly(SampleItemModel item)
        {
            string prefix = item.Type switch { SampleType.空白 => "BLK-", SampleType.标液 => "STD-", _ => "待测液-" };
            int maxIndex = 0;

            foreach (var s in Samples)
            {
                if (s != item && s.SampleName.StartsWith(prefix))
                {
                    string numPart = s.SampleName.Substring(prefix.Length);
                    if (int.TryParse(numPart, out int idx) && idx > maxIndex) maxIndex = idx;
                }
            }
            item.SampleName = $"{prefix}{maxIndex + 1}";
        }

        [RelayCommand] private void AddRow() => Samples.Add(CreateNewSample(SampleType.待测液, "新样品"));
        [RelayCommand] private void DeleteRow() { if (SelectedSample != null) Samples.Remove(SelectedSample); }
        [RelayCommand]
        private void ApplySequence()
        {
            WeakReferenceMessenger.Default.Send(new SampleSequenceChangedMessage(Samples.ToList()));
            MessageBox.Show("进样序列已成功下发至测量模块！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}