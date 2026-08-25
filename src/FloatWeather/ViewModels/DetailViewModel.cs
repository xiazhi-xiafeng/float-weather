using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FontFamily = System.Windows.Media.FontFamily;
using FloatWeather.Models.Dto;
using FloatWeather.Services;
using FloatWeather.Theme;

namespace FloatWeather.ViewModels;

/// <summary>详情窗口 VM</summary>
public partial class DetailViewModel : ObservableObject
{
    private readonly WeatherService _weather;
    private readonly ConfigService _config;
    private readonly IconService _icons;

    public ObservableCollection<ForecastHour> Hourly { get; } = new();
    public ObservableCollection<ForecastDay> Daily { get; } = new();
    public ObservableCollection<WeatherIndex> Indices { get; } = new();

    [ObservableProperty] private string cityName = "";
    [ObservableProperty] private string iconText = "🌤️";
    [ObservableProperty] private FontFamily? iconFont;
    [ObservableProperty] private string temperature = "--";
    [ObservableProperty] private string feelsLike = "";
    [ObservableProperty] private string weatherText = "";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private string aqi = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private WeatherTheme theme = ThemeResolver.Resolve(null, null, DateTime.Now);
    [ObservableProperty] private bool isLoading = true;

    public DetailViewModel(WeatherService weather, ConfigService config, IconService icons)
    {
        _weather = weather;
        _config = config;
        _icons = icons;
        CityName = config.Weather.CityName;
    }

    /// <summary>相对时间文案：N 分钟前 / N 小时前</summary>
    private static string Ago(DateTime t)
    {
        var span = DateTime.Now - t;
        if (span < TimeSpan.FromSeconds(60)) return "刚刚";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}分钟前";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}小时前";
        return $"{(int)span.TotalDays}天前";
    }

    public async Task LoadAsync()
    {
        try
        {
            var r = await _weather.RefreshAsync();
            if (r?.Now is null)
            {
                Status = "数据不可用";
                return;
            }
            var n = r.Now;
            CityName = n.City;
            await _icons.EnsureAsync();
            var glyph = _icons.Glyph(n.IconCode);
            if (glyph is not null)
            {
                IconText = glyph;
                IconFont = _icons.Font;
            }
            else
            {
                IconText = IconResolver.ToEmoji(n.IconCode);
                IconFont = null;
            }
            Temperature = $"{n.Temp:0.#}°";
            FeelsLike = $"体感 {n.FeelsLike:0.#}°";
            WeatherText = n.WeatherText;
            Detail = $"{n.WindDir} {n.WindScale:0.#}级 · 湿度 {n.Humidity}%";
            Aqi = n.Aqi > 0 ? $"空气 {n.AqiCategory} {n.Aqi}" : "";
            Status = $"{r.Source} · 更新于 {r.FetchedAt:MM-dd HH:mm} · {Ago(r.FetchedAt)}";
            Theme = ThemeResolver.Resolve(n.IconCode, n.WeatherText, DateTime.Now);

            Hourly.Clear();
            var now = DateTime.Now;
            foreach (var h in r.Hourly)
            {
                if (h.Time < now) continue;   // 已过去的小时不显示
                var g = _icons.Glyph(h.IconCode);
                h.IconText = g ?? IconResolver.ToEmoji(h.IconCode);
                h.IconFont = g is not null ? _icons.Font : null;
                Hourly.Add(h);
            }
            Daily.Clear();
            foreach (var d in r.Daily)
            {
                var g = _icons.Glyph(d.IconCode);
                d.IconText = g ?? IconResolver.ToEmoji(d.IconCode);
                d.IconFont = g is not null ? _icons.Font : null;
                Daily.Add(d);
            }
            Indices.Clear();
            foreach (var i in r.Indices) Indices.Add(i);
        }
        catch (Exception ex)
        {
            Status = "加载失败：" + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}