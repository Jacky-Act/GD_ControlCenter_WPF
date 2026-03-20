using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.ViewModels;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Controls;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class FlowInjectionView : UserControl
    {
        // 数据缓存：字典键为元素名称，值为该元素的 (Time, Intensity) 数据点列表
        private readonly ConcurrentDictionary<string, List<double>> _timeDict = new();
        private readonly ConcurrentDictionary<string, List<double>> _intensityDict = new();

        // 记录当前正在绘制哪个元素
        private string _currentPlotElement = string.Empty;

        // ScottPlot 的数据记录器，用于高性能动态追加数据
        private ScottPlot.Plottables.DataLogger? _dataLogger;

        public FlowInjectionView()
        {
            InitializeComponent();

            // 初始化图表样式
            RealTimePlot.Plot.Axes.Bottom.Label.Text = "时间 (s)";
            RealTimePlot.Plot.Axes.Left.Label.Text = "光谱强度 (Counts)";
            RealTimePlot.Plot.Grid.MajorLineColor = ScottPlot.Colors.White.WithAlpha(0.2);

            // 1. 订阅实时绘图点消息 (由 FlowInjectionLogic 发出)
            WeakReferenceMessenger.Default.Register<FlowInjectionPlotMessage>(this, (r, m) =>
            {
                var point = m.Value;

                // 存入内存缓存
                if (!_timeDict.ContainsKey(point.ElementName))
                {
                    _timeDict[point.ElementName] = new List<double>();
                    _intensityDict[point.ElementName] = new List<double>();
                }
                _timeDict[point.ElementName].Add(point.Time);
                _intensityDict[point.ElementName].Add(point.Intensity);

                // 如果新来的点正好是当前选中的元素，就画出来
                if (point.ElementName == _currentPlotElement && _dataLogger != null)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        _dataLogger.Add(point.Time, point.Intensity);

                        // 彻底绕开 ScottPlot 的版本问题，直接检查我们自己维护的字典
                        if (_timeDict.ContainsKey(point.ElementName) && _timeDict[point.ElementName].Count > 0)
                        {
                            RealTimePlot.Plot.Axes.AutoScale();
                            RealTimePlot.Refresh();
                        }
                    });
                }
            });

            // 2. 订阅元素切换消息 (由 ViewModel 下拉框改变时发出)
            WeakReferenceMessenger.Default.Register<SwitchPlotElementMessage>(this, (r, m) =>
            {
                // 修复点2：因为继承了 ValueChangedMessage<string>，所以这里用 m.Value
                Dispatcher.Invoke(() => SwitchElementChart(m.Value));
            });
        }

        /// <summary>
        /// 切换图表显示的元素
        /// </summary>
        private void SwitchElementChart(string newElement)
        {
            _currentPlotElement = newElement;

            // 清空旧图表
            RealTimePlot.Plot.Clear();

            // 新建一个数据记录器
            _dataLogger = RealTimePlot.Plot.Add.DataLogger();
            _dataLogger.Color = ScottPlot.Color.FromHex("#0071C2"); // 蓝色线条
            _dataLogger.LineWidth = 2;

            // 如果该元素已经有历史数据，一次性把历史数据全倒进去
            if (_timeDict.TryGetValue(newElement, out var times) &&
                _intensityDict.TryGetValue(newElement, out var intensities))
            {
                for (int i = 0; i < times.Count; i++)
                {
                    _dataLogger.Add(times[i], intensities[i]);
                }
            }

            RealTimePlot.Plot.Axes.AutoScale();
            RealTimePlot.Refresh();
        }
    }
}