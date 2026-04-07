using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer.Logic;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class SampleMeasurementViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;
        private readonly ElementConfigViewModel _elementConfigVM;

        [ObservableProperty] private ObservableCollection<SampleItemModel> _measurementSequence = new();
        [ObservableProperty] private SampleItemModel? _currentSample;
        [ObservableProperty] private bool _isCollecting;
        [ObservableProperty] private ObservableCollection<string> _pickedElements = new();
        [ObservableProperty] private string _selectedElement = string.Empty;

        // 内部数据缓冲区
        private List<double> _currentDataBuffer = new();

        public SampleMeasurementViewModel(JsonConfigService configService, ElementConfigViewModel elementConfigVM)
        {
            _configService = configService;
            _elementConfigVM = elementConfigVM;

            // 订阅从样品序列下发的任务名单
            WeakReferenceMessenger.Default.Register<SampleSequenceChangedMessage>(this, (r, m) => {
                MeasurementSequence = new ObservableCollection<SampleItemModel>(m.Value);
                if (MeasurementSequence.Count > 0 && CurrentSample == null)
                {
                    CurrentSample = MeasurementSequence[0];
                }
            });
        }

        // --- 供 View 层调用的公开方法 (核心修复) ---

        /// <summary>
        /// 获取当前选中元素或寻峰点的目标波长
        /// </summary>
        public double GetTargetWavelength()
        {
            return ParseWavelength(SelectedElement);
        }

        /// <summary>
        /// 将当前帧的强度值塞入采集缓冲区
        /// </summary>
        public void AddDataToBuffer(double intensity)
        {
            if (IsCollecting)
            {
                _currentDataBuffer.Add(intensity);
            }
        }

        // --- 内部逻辑 ---

        private double ParseWavelength(string elementInfo)
        {
            if (string.IsNullOrEmpty(elementInfo)) return 0;

            // 处理寻峰格式 "峰@283.31"
            if (elementInfo.Contains("@"))
            {
                var parts = elementInfo.Split('@');
                if (parts.Length > 1)
                {
                    string wlStr = parts[1].Replace("nm", "").Trim();
                    if (double.TryParse(wlStr, out double wl)) return wl;
                }
            }

            // 处理纯元素名 "Pb"
            var config = _elementConfigVM.SelectedConfigs.FirstOrDefault(x => x.ElementName == elementInfo);
            return config?.Wavelength ?? 0;
        }

        // --- 命令绑定 ---

        [RelayCommand]
        private void StartCollecting()
        {
            if (CurrentSample == null)
            {
                MessageBox.Show("请先选择一个样品！");
                return;
            }
            if (string.IsNullOrEmpty(SelectedElement))
            {
                MessageBox.Show("请先选择或寻峰一个要监控的元素！");
                return;
            }

            _currentDataBuffer.Clear();
            IsCollecting = true;
            CurrentSample.Status = "采集数据中...";
        }

        [RelayCommand]
        private void StopAndSave()
        {
            IsCollecting = false;

            if (_currentDataBuffer.Count > 0)
            {
                // 计算平均值
                double average = _currentDataBuffer.Average();

                // 更新结果展示
                var elementResult = CurrentSample?.ElementConcentrations.FirstOrDefault(e => e.ElementName == SelectedElement);
                if (elementResult != null)
                {
                    elementResult.MeasuredIntensity = average;
                }

                if (CurrentSample != null)
                {
                    CurrentSample.Status = "已完成";
                }

                // 物理保存到本地文件
                _configService.SaveResults(MeasurementSequence.ToList());

                // 自动跳转下一个
                int currentIndex = MeasurementSequence.IndexOf(CurrentSample!);
                if (currentIndex < MeasurementSequence.Count - 1)
                    CurrentSample = MeasurementSequence[currentIndex + 1];
                else
                    MessageBox.Show("全序列测量任务已完成！");
            }
        }

        [RelayCommand]
        private void StopSequence()
        {
            IsCollecting = false;
            if (CurrentSample != null && CurrentSample.Status == "采集数据中...")
                CurrentSample.Status = "已停止";
        }
    }
}