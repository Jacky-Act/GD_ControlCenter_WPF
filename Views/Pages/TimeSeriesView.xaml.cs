using GD_ControlCenter_WPF.ViewModels;
using ScottPlot.WPF;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

/*
 * 文件名: TimeSeriesView.xaml.cs
 * 模块: 视图层 (UI Code-Behind)
 * 描述: 多通道时序滚动图表的渲染引擎。
 * 由于底层光谱仪可能高达 100Hz (每秒100次) 狂刷数据，如果我们每收到一个点就强制重绘画布，
 * WPF 的 UI 线程会瞬间被海量的渲染指令淹没，导致软件假死卡顿。
 * 解决方案：ViewModel 只负责安静、迅速地把数据塞进内存 List 中。
 * 本 View 层开启一个独立的 DispatcherTimer（每 50ms 扫一次屏幕），
 * 时间一到，瞬间把 ViewModel 内存里的静态数组切片拿出来画在屏幕上。
 */

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class TimeSeriesView : UserControl
    {
        #region 1. 状态与定时器

        /// <summary>
        /// 当前屏幕上所有存活的图表控件集合 (由前台 XAML 的 ItemsControl 动态生成)。
        /// </summary>
        private readonly List<WpfPlot> _activePlots = new();

        /// <summary>
        /// UI 渲染节流定时器。
        /// 它的生命周期与当前页面绑定：页面打开时滴答，切走时休眠。
        /// </summary>
        private readonly DispatcherTimer _renderTimer = new();

        #endregion

        #region 2. 初始化与生命周期

        public TimeSeriesView()
        {
            InitializeComponent();

            // 设定 50ms 扫屏一次（约等于 20FPS，人眼看着已经非常流畅了）
            _renderTimer.Interval = TimeSpan.FromMilliseconds(50);

            _renderTimer.Tick += (s, e) =>
            {
                // 遍历屏幕上的每一个通道图表
                foreach (var plotControl in _activePlots)
                {
                    // 抓取该图表绑定的独立 ViewModel 数据源
                    if (plotControl.DataContext is TimeSeriesViewModel.TimeSeriesChartItem item)
                    {
                        // 脏检查：只有这 50ms 内真的来了新数据，才执行高耗能的重绘动作
                        if (item.HasNewData)
                        {
                            item.HasNewData = false;
                            RenderStaticChart(plotControl, item);
                        }
                    }
                }
            };

            // 页面加载时启动引擎，卸载时关闭以节省 CPU
            this.Loaded += (s, e) => _renderTimer.Start();
            this.Unloaded += (s, e) => _renderTimer.Stop();
        }

        #endregion

        #region 3. 动态控件挂载

        /// <summary>
        /// 当 ItemsControl 动态生成了一个新的波形图控件时触发。
        /// </summary>
        private void OnPlotLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfPlot plotControl) return;
            if (plotControl.DataContext is not TimeSeriesViewModel.TimeSeriesChartItem item) return;

            // 登记造册，加入定时器的巡更队列
            if (!_activePlots.Contains(plotControl)) _activePlots.Add(plotControl);

            // 无论从哪里切页面回来，直接根据内存里积攒的历史数据，强制画一幅全新的图
            RenderStaticChart(plotControl, item);
        }

        /// <summary>
        /// 当图表控件被销毁时触发（比如用户在设置里少选了几个追踪点）。
        /// </summary>
        private void OnPlotUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfPlot plotControl)
            {
                _activePlots.Remove(plotControl);
            }
        }

        #endregion

        #region 4. 图表核心渲染引擎 (Render Engine)

        /// <summary>
        /// 核心渲染逻辑：将内存中的离散点绘制为平滑滚动的折线图。
        /// </summary>
        private void RenderStaticChart(WpfPlot plotControl, TimeSeriesViewModel.TimeSeriesChartItem item)
        {
            // 1. 清空旧数据
            plotControl.Plot.Clear();

            double[] xs;
            double[] ys;

            // --- 2. 【核心隔离】：线程安全的静态切片 ---
            // 锁定 ViewModel 的资源池，用最快速度拷贝一份静态数组出来。
            // 这样我们在慢慢画图的时候，绝对不会阻塞后台硬件继续往 List 里塞数据。
            lock (item.SyncRoot)
            {
                if (item.TimePoints.Count == 0)
                {
                    // 若无数据，给个默认视野避免报错
                    plotControl.Plot.Axes.SetLimits(0, 5, 0, 100);
                    plotControl.Refresh();
                    return;
                }

                xs = item.TimePoints.ToArray();
                ys = item.IntensityPoints.ToArray();
            }

            // --- 3. 物理绘图 ---
            var scatter = plotControl.Plot.Add.Scatter(xs, ys);

            // 隐藏点只留线，视觉更清爽
            scatter.MarkerSize = 0;
            scatter.Color = item.LineColor;
            scatter.LineWidth = 1.5f;
            scatter.LegendText = item.Title;

            // --- 4. 配色与图例样式 ---
            plotControl.Plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Colors.Black;
            plotControl.Plot.Axes.Left.FrameLineStyle.Color = ScottPlot.Colors.Black;
            plotControl.Plot.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Colors.Black;
            plotControl.Plot.Axes.Bottom.FrameLineStyle.Color = ScottPlot.Colors.Black;
            plotControl.Plot.Grid.MajorLineColor = ScottPlot.Colors.Black.WithAlpha(0.1);

            plotControl.Plot.ShowLegend();
            plotControl.Plot.Legend.Alignment = ScottPlot.Alignment.UpperLeft;
            plotControl.Plot.Legend.FontColor = ScottPlot.Colors.Black;
            plotControl.Plot.Legend.BackgroundColor = ScottPlot.Colors.White;

            // --- 5. 强力接管坐标轴 (无限滚动心电图效果) ---

            // X 轴边界计算
            double minX = xs[0];  // 视窗左边界始终贴住内存中最老的一个点
            double maxX = xs[^1]; // 视窗右边界是当前最新时间

            // 计算当前视窗的时间跨度（秒），即使刚启动也至少给足 5 秒的预留宽度
            double xSpan = maxX - minX;
            if (xSpan < 5.0) xSpan = 5.0;

            // 右边界额外留出 5% 的空白“呼吸空间”，看着更舒服
            double rightEdge = maxX + xSpan * 0.05;

            // 强行锁死 X 轴，实现曲线往左平滑滚动
            plotControl.Plot.Axes.SetLimitsX(minX, rightEdge);

            // Y 轴自适应边界计算
            double minY = ys.Min();
            double maxY = ys.Max();
            double ySpan = maxY - minY;

            // 防止一条直线时 Y 轴缩放成天文数字
            if (ySpan < 0.0001) ySpan = 10.0;

            // 上下各留 10% 的安全边距
            plotControl.Plot.Axes.SetLimitsY(minY - ySpan * 0.1, maxY + ySpan * 0.1);

            // 推送画面
            plotControl.Refresh();
        }

        #endregion
    }
}