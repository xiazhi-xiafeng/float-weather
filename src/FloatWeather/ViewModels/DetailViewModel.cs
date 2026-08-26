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
    private readonly TempHistoryStore _history;

    public ObservableCollection<ForecastHour> Hourly { get; } = new();
    public ObservableCollection<ForecastDay> Daily { get; } = new();
    public ObservableCollection<WeatherIndex> Indices { get; } = new();

    // 温度趋势曲线数据（过去24h观察 / 未来7日高低温）
    public ObservableCollection<TrendPoint> PastTrend { get; } = new();
    public ObservableCollection<TrendPoint> FutureTrend { get; } = new();

    [ObservableProperty] private string cityName = "";
    [ObservableProperty] private string iconText = "🌤️";
    [ObservableProperty] private FontFamily? iconFont;
    [ObservableProperty] private string temperature = "--";
    [ObservableProperty] private string feelsLike = "";
    [ObservableProperty] private string weatherText = "";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private string aqi = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool hasPastTrend;
    [ObservableProperty] private bool hasFutureTrend;
    [ObservableProperty] private WeatherTheme theme = ThemeResolver.Resolve(null, null, DateTime.Now);
    [ObservableProperty] private bool isLoading = true;

    public DetailViewModel(WeatherService weather, ConfigService config, IconService icons, TempHistoryStore history)
    {
        _weather = weather;
        _config = config;
        _icons = icons;
        _history = history;
        CityName = config.Weather.CityName;
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
            Status = $"{r.Source} · 更新于 {r.FetchedAt:MM-dd HH:mm} · {TimeText.Ago(r.FetchedAt)}";
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

            BuildTrends(r);
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

    /// <summary>构建温度趋势曲线：过去24h（观察历史，单值线）+ 未来7日（预报高低温，温差带）。</summary>
    private void BuildTrends(WeatherResult r)
    {
        // 过去 24h：来自运行期温度观察历史（悬浮窗定时刷新写入）
        PastTrend.Clear();
        var now = DateTime.Now;
        var snap = _history.GetPast(24, now);
        foreach (var s in snap)
        {
            var label = s.Time.Date == now.Date ? s.Time.ToString("HH点") : s.Time.ToString("MM-dd");
            PastTrend.Add(new TrendPoint(label, s.Temp, null));
        }

        // 未来 7 日：预报表中的逐日高低温
        FutureTrend.Clear();
        foreach (var d in r.Daily)
        {
            var label = d.Date.Date == now.Date ? "今" : d.Date.ToString("MM-dd");
            FutureTrend.Add(new TrendPoint(label, (double)d.TempMax, (double)d.TempMin));
        }

        HasPastTrend = PastTrend.Count > 0;
        HasFutureTrend = FutureTrend.Count > 0;
    }
}