using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class SampleSequenceViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<SampleItemModel> _samples = new();

        [ObservableProperty]
        private SampleItemModel? _selectedSample;

        // 供下拉框绑定的枚举列表
        public Array SampleTypes => Enum.GetValues(typeof(SampleType));

        public List<int> AvailableRepeats { get; } = Enumerable.Range(1, 99).ToList();

        private SampleItemModel? _clipboardSample; // 内部剪贴板

        public SampleSequenceViewModel()
        {
            // 初始化给几个默认占位行
            Samples.Add(new SampleItemModel { SampleName = "空白液-1", Type = SampleType.空白 });
            Samples.Add(new SampleItemModel { SampleName = "标准液-1", Type = SampleType.标液, StandardConcentration = 1.0 });
            Samples.Add(new SampleItemModel { SampleName = "标准液-2", Type = SampleType.标液, StandardConcentration = 5.0 });
            Samples.Add(new SampleItemModel { SampleName = "待测液-1", Type = SampleType.待测液 });
        }


        // ================= 右键菜单与新增命令 =================

        [RelayCommand]
        private void AddRow()
        {
            Samples.Add(new SampleItemModel { SampleName = GetNextSampleName() });
        }
        [RelayCommand]
        private void InsertRow()
        {
            if (SelectedSample != null)
            {
                int index = Samples.IndexOf(SelectedSample);
                Samples.Insert(index, new SampleItemModel { SampleName = GetNextSampleName() });
            }
        }

        /// <summary>
        /// 智能命名算法：遍历当前列表，找到 "待测液-X" 中最大的 X，然后返回 X+1
        /// 这样即使中间删除了某几行，新加的行也不会发生名称冲突。
        /// </summary>
        private string GetNextSampleName()
        {
            int maxIndex = 0;
            string prefix = "待测液-";

            foreach (var sample in Samples)
            {
                if (sample.SampleName != null && sample.SampleName.StartsWith(prefix))
                {
                    // 截取 "待测液-" 后面的数字部分进行比对
                    string numberPart = sample.SampleName.Substring(prefix.Length);
                    if (int.TryParse(numberPart, out int index))
                    {
                        if (index > maxIndex)
                        {
                            maxIndex = index;
                        }
                    }
                }
            }

            // 返回当前最大序号 + 1
            return $"{prefix}{maxIndex + 1}";
        }

        [RelayCommand]
        private void DeleteRow()
        {
            if (SelectedSample != null) Samples.Remove(SelectedSample);
        }

        [RelayCommand]
        private void CopyRow()
        {
            if (SelectedSample != null) _clipboardSample = SelectedSample.Clone();
        }

        [RelayCommand]
        private void PasteRow()
        {
            if (_clipboardSample != null)
            {
                int index = SelectedSample != null ? Samples.IndexOf(SelectedSample) + 1 : Samples.Count;
                Samples.Insert(index, _clipboardSample.Clone());
            }
        }

        [RelayCommand]
        private void SetAsStandardStart()
        {
            if (SelectedSample != null)
            {
                // 把当前及往后的样本全部快速设为“标液”并自动递增命名
                int startIndex = Samples.IndexOf(SelectedSample);
                for (int i = startIndex; i < Samples.Count; i++)
                {
                    Samples[i].Type = SampleType.标液;
                    Samples[i].SampleName = $"STD-{i - startIndex + 1}";
                }
            }
        }

        // ================= 广播序列 =================
        [RelayCommand]
        private void ApplySequence()
        {
            // 将当前列表广播给“流动注射”和“连续测量”页面
            WeakReferenceMessenger.Default.Send(new SampleSequenceChangedMessage(Samples.ToList()));
            MessageBox.Show("进样序列已成功下发至测量模块！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}