using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FloatWeather.Theme;

/// <summary>天气类型</summary>
public enum WeatherKind
{
    Sunny,      // 晴
    Cloudy,     // 多云 / 少云
    Overcast,   // 阴
    Rain,       // 雨
    Snow,       // 雪
    Fog,        // 雾 / 霾 / 沙尘
    Thunder,    // 雷暴
    Unknown
}

/// <summary>
/// 根据天气码与昼夜计算动态主题。
/// 支持和风 IconCode（1xx 晴云 / 2xx 雷 / 3xx 雨 / 4xx 雪 / 5xx 雾霾），
/// 也支持通过文本关键词兜底识别。
/// </summary>
public static class ThemeResolver
{
    /// <summary>解析天气主题：iconCode 优先，weatherText 兜底，按本地时间判断昼夜。</summary>
    public static WeatherTheme Resolve(string? iconCode, string? weatherText, DateTime now)
    {
        var kind = Classify(iconCode, weatherText);
        bool day = now.Hour >= 6 && now.Hour < 19;
        return kind switch
        {
            WeatherKind.Sunny => day
                ? WeatherTheme.Create(Rgb(0x2E, 0x5E, 0x8C), Rgb(0x14, 0x21, 0x3D), Rgb(0xFF, 0xD1, 0x66))
                : WeatherTheme.Create(Rgb(0x1C, 0x2A, 0x4A), Rgb(0x0E, 0x14, 0x28), Rgb(0x7F, 0xB2, 0xFF)),
            WeatherKind.Cloudy => day
                ? WeatherTheme.Create(Rgb(0x3A, 0x55, 0x70), Rgb(0x1C, 0x2C, 0x3F), Rgb(0xC9, 0xD6, 0xE3))
                : WeatherTheme.Create(Rgb(0x24, 0x2F, 0x44), Rgb(0x12, 0x19, 0x26), Rgb(0x9F, 0xB6, 0xD0)),
            WeatherKind.Overcast => day
                ? WeatherTheme.Create(Rgb(0x4A, 0x55, 0x68), Rgb(0x2D, 0x37, 0x48), Rgb(0xB8, 0xC4, 0xD0))
                : WeatherTheme.Create(Rgb(0x2A, 0x30, 0x3C), Rgb(0x17, 0x1B, 0x24), Rgb(0x8E, 0x99, 0xA8)),
            WeatherKind.Rain => day
                ? WeatherTheme.Create(Rgb(0x33, 0x50, 0x6B), Rgb(0x1B, 0x2B, 0x3F), Rgb(0x7F, 0xB4, 0xE8))
                : WeatherTheme.Create(Rgb(0x1D, 0x2A, 0x40), Rgb(0x0F, 0x18, 0x26), Rgb(0x6F, 0xA3, 0xD8)),
            WeatherKind.Snow => day
                ? WeatherTheme.Create(Rgb(0x5A, 0x7B, 0x94), Rgb(0x2E, 0x42, 0x57), Rgb(0xEA, 0xF6, 0xFF))
                : WeatherTheme.Create(Rgb(0x33, 0x47, 0x5C), Rgb(0x18, 0x24, 0x2F), Rgb(0xD8, 0xEC, 0xFA)),
            WeatherKind.Fog => day
                ? WeatherTheme.Create(Rgb(0x6A, 0x6A, 0x72), Rgb(0x3C, 0x3C, 0x42), Rgb(0xD6, 0xD6, 0xDE))
                : WeatherTheme.Create(Rgb(0x3F, 0x40, 0x48), Rgb(0x24, 0x25, 0x29), Rgb(0xB8, 0xB8, 0xC2)),
            WeatherKind.Thunder => day
                ? WeatherTheme.Create(Rgb(0x3A, 0x2E, 0x52), Rgb(0x1B, 0x14, 0x26), Rgb(0xC9, 0xA7, 0xF0))
                : WeatherTheme.Create(Rgb(0x26, 0x1E, 0x3D), Rgb(0x12, 0x0D, 0x1E), Rgb(0xA9, 0x8B, 0xE0)),
            _ => day
                ? WeatherTheme.Create(Rgb(0x33, 0x4E, 0x68), Rgb(0x1A, 0x2A, 0x3E), Rgb(0x8A, 0xC7, 0xFF))
                : WeatherTheme.Create(Rgb(0x1F, 0x2C, 0x42), Rgb(0x11, 0x19, 0x28), Rgb(0x7F, 0xB2, 0xFF))
        };
    }

    private static WeatherKind Classify(string? iconCode, string? weatherText)
    {
        var code = iconCode?.Trim() ?? "";
        if (code.Length > 0 && char.IsAsciiDigit(code[0]))
        {
            var head = code[0];
            return head switch
            {
                '1' => WeatherKind.Cloudy,   // 晴/多云/阴（统一归多云调）
                '2' => WeatherKind.Thunder,
                '3' => WeatherKind.Rain,
                '4' => WeatherKind.Snow,
                '5' => WeatherKind.Fog,
                _ => WeatherKind.Unknown
            };
        }

        var text = weatherText ?? "";
        if (text.Contains("雷")) return WeatherKind.Thunder;
        if (text.Contains("雪") || text.Contains("霰")) return WeatherKind.Snow;
        if (text.Contains("雨") || text.Contains("雹")) return WeatherKind.Rain;
        if (text.Contains("雾") || text.Contains("霾") || text.Contains("沙") || text.Contains("尘")) return WeatherKind.Fog;
        if (text.Contains("晴")) return WeatherKind.Sunny;
        if (text.Contains("云")) return WeatherKind.Cloudy;
        if (text.Contains("阴")) return WeatherKind.Overcast;
        return WeatherKind.Unknown;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
