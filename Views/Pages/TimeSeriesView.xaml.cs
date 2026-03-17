using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using ScottPlot;
using ScottPlot.MultiplotLayouts; // 需要引用此命名空间以使用布局类
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class TimeSeriesView : UserControl
    {
        // 保存每个子图的 DataLogger 引用，以便后续推送数据
        private List<ScottPlot.Plottables.DataLogger> _dataLoggers = new();
        private int _channelCount = 4; // 假设有4条时序线

        public TimeSeriesView()
        {
            InitializeComponent();
            InitializeMultiPlot();

            // 注册视图切换消息
            WeakReferenceMessenger.Default.Register<ChangeTimeSeriesViewMessage>(this, (r, m) =>
            {
                SwitchPlotLayout(m.IsMatrixView);
            });
        }

        private void InitializeMultiPlot()
        {
            TimeSeriesMultiPlot.Multiplot.AddPlots(_channelCount);
            string[] hexColors = { "#0071C2", "#E9262E", "#369F2D", "#FC8002", "#7E318A", "#32AEEC" };

            for (int i = 0; i < _channelCount; i++)
            {
                var plot = TimeSeriesMultiPlot.Multiplot.GetPlot(i);
                var logger = plot.Add.DataLogger();

                logger.Color = Color.FromHex(hexColors[i % hexColors.Length]);
                logger.LineWidth = 1.5f;
                logger.LegendText = $"通道 {i + 1}";

                plot.ShowLegend();

                // --- 坐标轴颜色统一设置为黑色 ---
                // Y 轴 (纵坐标) 黑色
                plot.Axes.Left.TickLabelStyle.ForeColor = Colors.Black;
                plot.Axes.Left.FrameLineStyle.Color = Colors.Black;
                plot.Axes.Left.MajorTickStyle.Color = Colors.Black;
                plot.Axes.Left.MinorTickStyle.Color = Colors.Black;

                // X 轴 (横坐标) 黑色
                plot.Axes.Bottom.TickLabelStyle.ForeColor = Colors.Black;
                plot.Axes.Bottom.FrameLineStyle.Color = Colors.Black;
                plot.Axes.Bottom.MajorTickStyle.Color = Colors.Black;
                plot.Axes.Bottom.MinorTickStyle.Color = Colors.Black;

                // 网格线改为浅黑色（适配你的白色背景）
                plot.Grid.MajorLineColor = Colors.Black.WithAlpha(0.1);

                _dataLoggers.Add(logger);
            }

            // 初始化时调用一次布局刷新，动态应用边距和坐标轴共享
            SwitchPlotLayout(isMatrix: false);

            // 逻辑上共用横轴（滑动/缩放同步）
            // 使所有子图的 X 轴在缩放和平移时保持同步
            for (int i = 1; i < _channelCount; i++)
            {
                var basePlot = TimeSeriesMultiPlot.Multiplot.GetPlot(0);
                var currentPlot = TimeSeriesMultiPlot.Multiplot.GetPlot(i);

                // 当任意图表的轴范围改变时，同步其他图表 (ScottPlot 5 标准做法)
                currentPlot.RenderManager.AxisLimitsChanged += (s, e) =>
                {
                    basePlot.Axes.SetLimitsX(currentPlot.Axes.GetLimits());
                };
                basePlot.RenderManager.AxisLimitsChanged += (s, e) =>
                {
                    currentPlot.Axes.SetLimitsX(basePlot.Axes.GetLimits());
                };
            }
        }

        /// <summary>
        /// 切换图表布局排版，并动态调整间距与坐标轴显示
        /// </summary>
        /// <summary>
        /// 切换图表布局排版，并动态调整间距与坐标轴显示
        /// </summary>
        private void SwitchPlotLayout(bool isMatrix)
        {
            Dispatcher.Invoke(() =>
            {
                int cols = isMatrix ? 2 : 1;
                int rows = (int)Math.Ceiling((double)_channelCount / cols);

                // 使用自定义布局引擎，保证数据区高度绝对一致
                TimeSeriesMultiPlot.Multiplot.Layout = new SeamlessMultiplotLayout(rows, cols, _channelCount);

                for (int i = 0; i < _channelCount; i++)
                {
                    var plot = TimeSeriesMultiPlot.Multiplot.GetPlot(i);

                    int row = i / cols;
                    int col = i % cols;

                    bool isTopPlotInColumn = (row == 0);
                    bool isBottomPlotInColumn = (row == rows - 1) || (i + cols >= _channelCount);

                    float padTop = isTopPlotInColumn ? 15f : 0f;
                    float padBottom = isBottomPlotInColumn ? 30f : 0f;

                    // 1. 设置边距
                    plot.Layout.Fixed(new PixelPadding
                    {
                        Left = 60,
                        Right = 15,
                        Bottom = padBottom,
                        Top = padTop
                    });

                    // 2. 最底部的图表显示横坐标
                    plot.Axes.Bottom.TickLabelStyle.IsVisible = isBottomPlotInColumn;
                    plot.Axes.Bottom.MajorTickStyle.Length = isBottomPlotInColumn ? 5 : 0;
                    plot.Axes.Bottom.MinorTickStyle.Length = isBottomPlotInColumn ? 2 : 0;

                    // 3. 加粗加黑分隔线与外边框
                    // 为了防止双倍线宽重叠，上方的图表顶部边框只在第一排显示
                    plot.Axes.Top.FrameLineStyle.Width = isTopPlotInColumn ? 2 : 0;
                    plot.Axes.Bottom.FrameLineStyle.Width = 2; // 分隔线宽度为 2

                    // 顺便把左右边框也加宽为 2，保持整体相框美观协调
                    plot.Axes.Left.FrameLineStyle.Width = 2;
                    plot.Axes.Right.FrameLineStyle.Width = 2;
                }

                TimeSeriesMultiPlot.Refresh();
            });
        }
    }

    /// <summary>
    /// 专为共用横坐标设计的无缝布局器，保证各图表的实际数据区域高度绝对均等
    /// </summary>
    class SeamlessMultiplotLayout : IMultiplotLayout
    {
        private readonly int _rows;
        private readonly int _cols;
        private readonly int _totalCount;

        public SeamlessMultiplotLayout(int rows, int cols, int totalCount)
        {
            _rows = rows;
            _cols = cols;
            _totalCount = totalCount;
        }

        public PixelRect[] GetSubplotRectangles(SubplotCollection subplots, PixelRect figureRect)
        {
            PixelRect[] rects = new PixelRect[subplots.Count];

            // 这里的数值必须与 SwitchPlotLayout 中的 Fixed(Padding) 对应
            float padTop = 15f;
            float padBottom = 30f;

            float colWidth = figureRect.Width / _cols;

            // 剔除顶部和底部的留白后，将纯数据区的高度均分
            float totalDataHeight = figureRect.Height - padTop - padBottom;
            float dataHeightPerRow = totalDataHeight / _rows;

            for (int i = 0; i < subplots.Count; i++)
            {
                int row = i / _cols;
                int col = i % _cols;

                bool isTop = (row == 0);
                bool isBottom = (row == _rows - 1) || (i + _cols >= _totalCount);

                float plotPadTop = isTop ? padTop : 0;
                float plotPadBottom = isBottom ? padBottom : 0;

                // 当前图表的最终高度 = 均分的数据区高度 + 自身所需的边距高度
                float plotHeight = dataHeightPerRow + plotPadTop + plotPadBottom;

                // 计算 Y 轴起始点
                float topY = figureRect.Top + (isTop ? 0 : padTop + row * dataHeightPerRow);
                // 计算 X 轴起始点
                float leftX = figureRect.Left + col * colWidth;

                // 赋予图表坐标
                rects[i] = new PixelRect(new PixelSize(colWidth, plotHeight)).WithDelta(leftX, topY);
            }
            return rects;
        }
    }
}