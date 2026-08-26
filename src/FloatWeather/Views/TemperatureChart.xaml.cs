using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FloatWeather.Models.Dto;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FloatWeather.Views;

/// <summary>
/// 轻量温度趋势自绘控件：把一组 <see cref="TrendPoint"/> 渲染成折线/温差带状。
/// 单值序列画"曲线 + 线下面积"，双值序列画"高低温带状 + 高低两条折线"，坐标轴自动按数据缩放。
/// </summary>
public partial class TemperatureChart : UserControl
{
    // 高低温曲线配色（与 7 日预报的渐变高低色保持一致）
    private static readonly Color HighColor = Color.FromRgb(0xFF, 0xC9, 0x66); // 亮橙
    private static readonly Color LowColor  = Color.FromRgb(0x4D, 0x8D, 0xFF);  // 亮蓝

    private IReadOnlyList<TrendPoint> _points = Array.Empty<TrendPoint>();

    /// <summary>绑定的趋势点集合；变化时自动重绘。</summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IReadOnlyList<TrendPoint>),
            typeof(TemperatureChart),
            new PropertyMetadata(null, (d, _) => { if (d is TemperatureChart c) c.Redraw(); }));

    public IReadOnlyList<TrendPoint>? ItemsSource
    {
        get => (IReadOnlyList<TrendPoint>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public TemperatureChart()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>设置要绘制的数据点并重绘。</summary>
    public void Render(IReadOnlyList<TrendPoint> points)
    {
        _points = points ?? Array.Empty<TrendPoint>();
        Redraw();
    }

    private void Redraw()
    {
        if (ChartCanvas is null) return;
        ChartCanvas.Children.Clear();
        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w < 10 || h < 10) return;

        _points = ItemsSource ?? Array.Empty<TrendPoint>();
        if (_points.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "暂无数据",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
            };
            empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(empty, (w - empty.DesiredSize.Width) / 2);
            Canvas.SetTop(empty, (h - empty.DesiredSize.Height) / 2);
            ChartCanvas.Children.Add(empty);
            return;
        }

        bool hasBand = _points.Any(p => p.Low.HasValue);

        // 数据范围（留上下边距，避免折线贴边）
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in _points)
        {
            max = Math.Max(max, p.High);
            if (p.Low.HasValue) min = Math.Min(min, p.Low.Value);
            else min = Math.Min(min, p.High);
        }
        if (min > max) return;
        double span = Math.Max(1, max - min);
        min -= span * 0.18;
        max += span * 0.12;

        const double padL = 6, padR = 6, padT = 4, padB = 16;
        double cw = w - padL - padR;
        double ch = h - padT - padB;
        double X(int i) => padL + (double)i / (double)(_points.Count - 1) * cw;
        double Y(double v) => padT + (max - v) / (max - min) * ch;

        // 3) 网格线（横向两条淡线）
        for (int g = 0; g <= 2; g++)
        {
            double gy = padT + ch * g / 2.0;
            ChartCanvas.Children.Add(new Line
            {
                X1 = padL, Y1 = gy, X2 = padL + cw, Y2 = gy,
                Stroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 3 },
            });
        }

        if (hasBand)
        {
            // 高低温坐标：低线至少位于高线下方 minGap 像素，温差过小时也保证不重叠、数字可读
            int n = _points.Count;
            var highPts = new Point[n];
            var lowPts = new Point[n];
            const double minGap = 4.0;
            for (int i = 0; i < n; i++)
            {
                double hHigh = Y(_points[i].High);
                double rawLow = Y(_points[i].Low ?? _points[i].High);
                highPts[i] = new Point(X(i), hHigh);
                lowPts[i] = new Point(X(i), Math.Max(rawLow, hHigh + minGap));
            }

            // 温差带：中性淡色半透明底衬，仅作"高低温区间"提示
            var band = new PathGeometry();
            var fig = new PathFigure { IsClosed = true, IsFilled = true };
            fig.StartPoint = highPts[0];
            for (int i = 1; i < n; i++) fig.Segments.Add(new LineSegment(highPts[i], true));
            for (int i = n - 1; i >= 0; i--) fig.Segments.Add(new LineSegment(lowPts[i], true));
            band.Figures.Add(fig);
            ChartCanvas.Children.Add(new Path { Data = band, Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xB8, 0xC6, 0xD6)) });

            ChartCanvas.Children.Add(BuildPolyline(highPts, HighColor, 2.6));
            ChartCanvas.Children.Add(BuildPolyline(lowPts, LowColor, 2.4));
            for (int i = 0; i < n; i++)
            {
                AddDot(highPts[i].X, highPts[i].Y, HighColor, 2.6);
                AddDot(lowPts[i].X, lowPts[i].Y, LowColor, 2.4);
                // 温度数字：高温标点上方、低温标点下方，配合最小间距互不遮挡
                AddTempLabel(_points[i].High, highPts[i].X, highPts[i].Y, HighColor, below: false);
                AddTempLabel(_points[i].Low ?? _points[i].High, lowPts[i].X, lowPts[i].Y, LowColor, below: true);
            }
        }
        else
        {
            // 单值线 + 线下渐变面积（过去24h观察序列）
            var area = BuildArea(_points.Select(p => p.High), X, Y, padT + ch);
            ChartCanvas.Children.Add(area);
            ChartCanvas.Children.Add(BuildPolyline(_points.Select(p => p.High), X, Y, HighColor, 2.4));

            // 点少时逐点标数字便于查看；点多时逐点数字会挤在一起，改用整体最高/最低角标
            bool showLabels = _points.Count <= 8;
            for (int i = 0; i < _points.Count; i++)
            {
                double px = X(i), py = Y(_points[i].High);
                AddDot(px, py, HighColor, 2.4);
                if (showLabels) AddTempLabel(_points[i].High, px, py, HighColor, below: false);
            }
        }

        // 4) 坐标轴标签：首、中、末三点的 X 标签
        for (int i = 0; i < _points.Count; i++)
        {
            if (i != 0 && i != _points.Count - 1 && i != _points.Count / 2) continue;
            var label = new TextBlock
            {
                Text = _points[i].Label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Max(0, Math.Min(cw - label.DesiredSize.Width, X(i) - label.DesiredSize.Width / 2)));
            Canvas.SetTop(label, padT + ch + 3);
            ChartCanvas.Children.Add(label);
        }

        // 最高 / 最低温度标注（单值序列且点多无法逐点标注时，用整体角标；点位少时已逐点标数字，避免覆盖）
        if (!hasBand && _points.Count > 8)
        {
            double overallMin = double.MaxValue, overallMax = double.MinValue;
            foreach (var p in _points)
            {
                overallMax = Math.Max(overallMax, p.High);
                overallMin = Math.Min(overallMin, p.Low ?? p.High);
            }
            AddCornerText($"{overallMax:0.#}°", HighColor, padL, -1);
            AddCornerText($"{overallMin:0.#}°", HighColor, cw, 1);
        }
    }

    private void AddCornerText(string text, Color color, double leftOffset, int align)
    {
        if (string.IsNullOrEmpty(text)) return;
        var t = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
        };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(t, align < 0 ? Math.Max(0, leftOffset) : Math.Max(0, ChartCanvas.ActualWidth - t.DesiredSize.Width - leftOffset));
        Canvas.SetTop(t, 0);
        ChartCanvas.Children.Add(t);
    }

    private void AddDot(double x, double y, Color color, double r)
    {
        var dot = new Ellipse
        {
            Width = r * 2.4,
            Height = r * 2.4,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            // 原色圆点、白色描边，与曲线同色即可在深浅背景上都清晰
        };
        Canvas.SetLeft(dot, x - dot.Width / 2);
        Canvas.SetTop(dot, y - dot.Height / 2);
        ChartCanvas.Children.Add(dot);
    }

    private static Polyline BuildPolyline(IEnumerable<double> values, Func<int, double> X, Func<double, double> Y, Color color, double thickness)
    {
        var pl = new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        int i = 0;
        foreach (var v in values)
            pl.Points.Add(new Point(X(i++), Y(v)));
        return pl;
    }

    private static Polyline BuildPolyline(IReadOnlyList<Point> points, Color color, double thickness)
    {
        var pl = new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        foreach (var p in points)
            pl.Points.Add(p);
        return pl;
    }

    /// <summary>在数据点附近标注温度数字：高温标上方、低温标下方，边缘自动收拢到画布内。</summary>
    private void AddTempLabel(double temp, double x, double y, Color color, bool below)
    {
        var t = new TextBlock
        {
            Text = $"{temp:0.#}°",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
        };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double px = Math.Clamp(x - t.DesiredSize.Width / 2, 0, Math.Max(0, ChartCanvas.ActualWidth - t.DesiredSize.Width));
        double py = below ? y + 3 : y - t.DesiredSize.Height - 3;
        Canvas.SetLeft(t, px);
        Canvas.SetTop(t, py);
        ChartCanvas.Children.Add(t);
    }

    private static Path BuildArea(IEnumerable<double> values, Func<int, double> X, Func<double, double> Y, double baselineY)
    {
        var pts = values.ToList();
        var geo = new PathGeometry();
        var fig = new PathFigure { IsClosed = true, IsFilled = true };
        fig.StartPoint = new Point(X(0), Y(pts[0]));
        for (int i = 1; i < pts.Count; i++)
            fig.Segments.Add(new LineSegment(new Point(X(i), Y(pts[i])), true));
        // 沿曲线末端垂直到底，再水平回到起点，形成封闭渐变面
        fig.Segments.Add(new LineSegment(new Point(X(pts.Count - 1), baselineY), true));
        fig.Segments.Add(new LineSegment(new Point(X(0), baselineY), true));
        geo.Figures.Add(fig);

        var fill = new LinearGradientBrush();
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0x4D, 0x8D, 0xFF), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0x4D, 0x8D, 0xFF), 0.35));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x02, 0x4D, 0x8D, 0xFF), 1));
        return new Path { Data = geo, Fill = fill };
    }
}