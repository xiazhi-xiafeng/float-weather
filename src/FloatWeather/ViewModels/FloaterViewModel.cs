using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FontFamily = System.Windows.Media.FontFamily;
using FloatWeather.Models.Dto;
using FloatWeather.Services;
using FloatWeather.Theme;

namespace FloatWeather.ViewModels;

/// <summary>悬浮小组件上逐时浮层的单行数据</summary>
public sealed record HourPopupItem(string Label, string IconText, FontFamily? IconFont, string Temp, string AqiText);

/// <summary>悬浮小组件 VM</summary>
public partial class FloaterViewModel : ObservableObject
{
    private readonly WeatherService _weather;
    private readonly ConfigService _config;
    private readonly IconService _icons;
    private readonly IpLocationService _ip;
    private bool _ipLocated;   // 首次刷新前是否已尝试过 IP 定位（只定位一次）

    public ObservableCollection<HourPopupItem> Hourly { get; } = new();

    [ObservableProperty] private string temperature = "--";
    [ObservableProperty] private string weatherText = "加载中";
    [ObservableProperty] private string cityName = "";
    [ObservableProperty] private string iconText = "🌤️";
    [ObservableProperty] private FontFamily? iconFont;
    [ObservableProperty] private bool hasHourly;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string humidity = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool hasHumidity;
    [ObservableProperty] private WeatherTheme theme = ThemeResolver.Resolve(null, null, DateTime.Now);

    /// <summary>是否桌面直显（无玻璃卡片）</summary>
    [ObservableProperty] private bool isBare;
    /// <summary>全局透明度 0.2–1.0</summary>
    [ObservableProperty] private double opacity = 1.0;

    // 前景色：Bare 模式下切换为深色，保证浅色/白色桌面可读（原主题色偏白）
    [ObservableProperty] private System.Windows.Media.Brush fgHigh;
    [ObservableProperty] private System.Windows.Media.Brush fgMid;
    [ObservableProperty] private System.Windows.Media.Brush fgLow;
    [ObservableProperty] private System.Windows.Media.Brush fgFaint;
    [ObservableProperty] private System.Windows.Media.Brush iconBrush;
    [ObservableProperty] private System.Windows.Media.Brush iconSoftBrush;

    /// <summary>桌面直显时：背景偏暗则为 true，切换为白色文字以保证可读（由窗口采样桌面亮度驱动）</summary>
    [ObservableProperty] private bool isDarkBackground;

    public FloaterViewModel(WeatherService weather, ConfigService config, IconService icons, IpLocationService ip)
    {
        _weather = weather;
        _config = config;
        _icons = icons;
        _ip = ip;
        CityName = config.IsCitySetFromUser() ? config.Weather.CityName : "定位中…";
        UpdateTextBrushes();
    }

    partial void OnIsBareChanged(bool value) => UpdateTextBrushes();
    partial void OnIsDarkBackgroundChanged(bool value) => UpdateTextBrushes();
    partial void OnThemeChanged(WeatherTheme value) { if (!IsBare) UpdateTextBrushes(); }

    /// <summary>按当前是否桌面直显与背景明暗，刷新文字与图标前景色（Bare 下白色桌→深字，暗色桌→白字）</summary>
    private void UpdateTextBrushes()
    {
        if (IsBare)
        {
            if (IsDarkBackground)
            {
                // 暗背景：白色文字，无任何修饰，所见即所得
                FgHigh = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                FgMid = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
                FgLow = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF));
                FgFaint = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xDB, 0xFF, 0xFF, 0xFF));
                IconBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF5, 0x9E, 0xE4, 0xF8));
                IconSoftBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0x9E, 0xE4, 0xF8));
            }
            else
            {
                // 亮背景：深灰文字保证白色/浅色桌面清晰
                FgHigh = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF2, 0x16, 0x16, 0x16));
                FgMid = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE0, 0x1F, 0x1F, 0x1F));
                FgLow = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xD0, 0x2A, 0x2A, 0x2A));
                FgFaint = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xBF, 0x33, 0x33, 0x33));
                IconBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE8, 0x0B, 0x4F, 0x6E));
                IconSoftBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA8, 0x0B, 0x4F, 0x6E));
            }
        }
        else
        {
            FgHigh = Res("TextHighBrush");
            FgMid = Res("TextMidBrush");
            FgLow = Res("TextLowBrush");
            FgFaint = Res("TextFaintBrush");
            IconBrush = Theme.AccentBrush;
            IconSoftBrush = Theme.AccentSoftBrush;
        }
    }

    private static System.Windows.Media.Brush Res(string key) =>
        (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[key];

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = "更新中…";

            // 未设置城市：先用公网 IP 定位城市并持久化，再按该城市刷新（整次运行只定位一次）
            if (!_config.IsCitySetFromUser() && !_ipLocated)
            {
                _ipLocated = true;
                var located = await _ip.ResolveCityAsync();
                if (!string.IsNullOrWhiteSpace(located))
                {
                    var w = _config.Weather;
                    w.CityName = located;
                    _config.SaveWeather(w);
                    CityName = located;
                }
                else
                {
                    CityName = _config.Weather.CityName;   // 定位失败回退内置默认
                }
            }

            var result = await _weather.RefreshAsync();
            var n = result?.Now;
            Temperature = n is null ? "--" : $"{n.Temp:0.#}°";
            WeatherText = n?.WeatherText ?? "暂无数据";
            HasHumidity = (n?.Humidity ?? 0) > 0;
            Humidity = HasHumidity ? $"湿度 {n!.Humidity}%" : "";
            CityName = _config.Weather.CityName;
            // 悬浮窗列宽有限，只保留“数据源 + 更新时间”两项关键信息，避免结尾被裁切
            Status = result is null ? "数据不可用" : $"{result.Source} · 更新于 {result.FetchedAt:HH:mm}";

            // 官方图标字体：先确保字体资源就绪，再切字形（失败回退 emoji）
            if (n is not null)
                await _icons.EnsureAsync();

            // 动态天气配色：随天气类型与昼夜变化
            Theme = ThemeResolver.Resolve(n?.IconCode, n?.WeatherText, DateTime.Now);

            // 主图标
            var main = n is null ? null : _icons.Glyph(n.IconCode);
            if (main is not null)
            {
                IconText = main;
                IconFont = _icons.Font;
            }
            else
            {
                IconText = n is null ? "🌤️" : IconResolver.ToEmoji(n.IconCode);
                IconFont = null;
            }

            // 逐时浮层（隐藏已过去时间的小时）
            Hourly.Clear();
            var now = DateTime.Now;
            var nowTemp = n?.Temp ?? 0m;
            foreach (var h in result?.Hourly ?? new())
            {
                if (h.Time < now) continue;   // 已过去的小时不显示
                var glyph = _icons.Glyph(h.IconCode);
                Hourly.Add(new HourPopupItem(
                    h.Time.ToString("HH点"),
                    glyph ?? IconResolver.ToEmoji(h.IconCode),
                    glyph is not null ? _icons.Font : null,
                    $"{h.Temp:0.#}°",
                    h.Temp >= nowTemp ? "▲" : "▼"));
            }
            HasHourly = Hourly.Count > 0;
        }
        catch (Exception ex)
        {
            Status = "获取失败";
            WeatherText = ex.Message;
            IconText = "🌤️";
            IconFont = null;
            HasHumidity = false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}