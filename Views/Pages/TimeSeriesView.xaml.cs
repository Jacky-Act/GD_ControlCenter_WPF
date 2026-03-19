using GD_ControlCenter_WPF.ViewModels;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class TimeSeriesView : UserControl
    {
        private readonly List<WpfPlot> _activePlots = new();
        private readonly DispatcherTimer _renderTimer = new();

        public TimeSeriesView()
        {
            InitializeComponent();

            _renderTimer.Interval = TimeSpan.FromMilliseconds(50);
            _renderTimer.Tick += (s, e) =>
            {
                foreach (var plotControl in _activePlots)
                {
                    if (plotControl.DataContext is TimeSeriesViewModel.TimeSeriesChartItem item)
                    {
                        if (item.HasNewData)
                        {
                            item.HasNewData = false;
                            RenderStaticChart(plotControl, item);
                        }
                    }
                }
            };

            this.Loaded += (s, e) => _renderTimer.Start();
            this.Unloaded += (s, e) => _renderTimer.Stop();
        }

        private void OnPlotLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfPlot plotControl) return;
            if (plotControl.DataContext is not TimeSeriesViewModel.TimeSeriesChartItem item) return;

            if (!_activePlots.Contains(plotControl)) _activePlots.Add(plotControl);

            // 无论从哪里切回来，直接根据内存里的数据画一幅全新的图
            RenderStaticChart(plotControl, item);
        }

        private void OnPlotUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfPlot plotControl)
            {
                _activePlots.Remove(plotControl);
            }
        }

        /// <summary>
        /// 核心渲染逻辑：把 ViewModel 的数据当成静态数据画出来，彻底摆脱引擎干扰
        /// </summary>
        private void RenderStaticChart(WpfPlot plotControl, TimeSeriesViewModel.TimeSeriesChartItem item)
        {
            plotControl.Plot.Clear();

            double[] xs;
            double[] ys;

            // --- 1. 线程安全地获取数据快照 ---
            lock (item.SyncRoot)
            {
                if (item.TimePoints.Count == 0)
                {
                    plotControl.Plot.Axes.SetLimits(0, 5, 0, 100);
                    plotControl.Refresh();
                    return; // 没数据直接退出
                }

                // 瞬间将数据复制为静态数组，释放锁，绝不阻塞底层硬件采集
                xs = item.TimePoints.ToArray();
                ys = item.IntensityPoints.ToArray();
            }

            // --- 2. 画数据 (改为纯折线) ---
            var scatter = plotControl.Plot.Add.Scatter(xs, ys);

            // 【修改点】：将数据点的大小设为0，即隐藏圆点，仅保留折线。
            // (注: 若使用的是最新的 ScottPlot5，也可使用 plotControl.Plot.Add.ScatterLine(xs, ys);)
            scatter.MarkerSize = 0;

            scatter.Color = item.LineColor;
            scatter.LineWidth = 1.5f;
            scatter.LegendText = item.Title;

            // --- 3. 配置样式 ---
            plotControl.Plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Colors.Black;
            plotControl.Plot.Axes.Left.FrameLineStyle.Color = ScottPlot.Colors.Black;
            plotControl.Plot.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Colors.Black;
            plotControl.Plot.Axes.Bottom.FrameLineStyle.Color = ScottPlot.Colors.Black;
            plotControl.Plot.Grid.MajorLineColor = ScottPlot.Colors.Black.WithAlpha(0.1);

            plotControl.Plot.ShowLegend();
            plotControl.Plot.Legend.Alignment = ScottPlot.Alignment.UpperLeft;
            plotControl.Plot.Legend.FontColor = ScottPlot.Colors.Black;
            plotControl.Plot.Legend.BackgroundColor = ScottPlot.Colors.White;

            // --- 4. 强力接管坐标轴 (滚动窗口展示) ---

            double minX = xs[0];  // 【修改】：左边界不再是 0，而是当前内存里最老的一个点
            double maxX = xs[^1]; // 右边界是最新时间

            // 计算当前视窗的跨度（秒），最少给 5 秒宽度
            double xSpan = maxX - minX;
            if (xSpan < 5.0) xSpan = 5.0;

            // 右侧留 5% 的呼吸空间
            double rightEdge = maxX + xSpan * 0.05;

            // 【修改】：X 轴左侧跟着 minX 走，实现完美平滑滚动
            plotControl.Plot.Axes.SetLimitsX(minX, rightEdge);

            // Y 轴自适应保持不变
            double minY = ys.Min();
            double maxY = ys.Max();
            double ySpan = maxY - minY;

            if (ySpan < 0.0001) ySpan = 10.0;

            plotControl.Plot.Axes.SetLimitsY(minY - ySpan * 0.1, maxY + ySpan * 0.1);

            plotControl.Refresh();
        }
    }
}