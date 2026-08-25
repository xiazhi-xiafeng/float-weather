using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace FloatWeather.Theme;

/// <summary>
/// 动态天气配色：一组冻结画刷，随天气类型与昼夜变化。
/// 玻璃底色统一为深色调，文字固定白色系，仅色调与强调色变化。
/// </summary>
public sealed class WeatherTheme
{
    /// <summary>主玻璃渐变（卡片/悬浮窗背景）</summary>
    public required Brush GlassBrush { get; init; }

    /// <summary>内层半透明面板（浮层/列表底）</summary>
    public required Brush InnerBrush { get; init; }

    /// <summary>强调色（图标/温度/高亮）</summary>
    public required Brush AccentBrush { get; init; }

    /// <summary>柔和强调（次级图标/装饰）</summary>
    public required Brush AccentSoftBrush { get; init; }

    /// <summary>高光描边</summary>
    public required Brush StrokeBrush { get; init; }

    /// <summary>构建主题。baseTop/baseBottom 为玻璃上下渐变色，accent 为强调色。</summary>
    public static WeatherTheme Create(Color baseTop, Color baseBottom, Color accent)
    {
        var glass = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x78, baseTop.R, baseTop.G, baseTop.B), 0),
                new GradientStop(Color.FromArgb(0xC8, baseBottom.R, baseBottom.G, baseBottom.B), 1)
            }
        };
        glass.Freeze();

        var inner = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        inner.Freeze();

        var accentBrush = new SolidColorBrush(accent);
        accentBrush.Freeze();

        var accentSoft = new SolidColorBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B));
        accentSoft.Freeze();

        var stroke = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.6),
                new GradientStop(Color.FromArgb(0x11, 0xFF, 0xFF, 0xFF), 1)
            }
        };
        stroke.Freeze();

        return new WeatherTheme
        {
            GlassBrush = glass,
            InnerBrush = inner,
            AccentBrush = accentBrush,
            AccentSoftBrush = accentSoft,
            StrokeBrush = stroke
        };
    }
}
