using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FloatWeather.Models.Dto;
using FloatWeather.Models.Dto.Sources;
using FloatWeather.Services;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Providers;

/// <summary>
/// OpenWeather 天气（data/2.5）。q=城市名直接查询，无需城市 ID 解析。
/// 实时 weather + 预报 forecast（3 小时步长，聚合为逐时与逐日）。
/// 图标码由 OpenWeather 码映射为和风 icon code，复用官方图标字体。
/// </summary>
public sealed class OpenWeatherProvider : IWeatherProvider
{
    private const string Base = "https://api.openweathermap.org/data/2.5";

    public string Name => "OpenWeather";

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_config.OpenWeather.Key) &&
        !_config.OpenWeather.Key.Contains("你的OpenWeatherKey");

    private readonly HttpClient _http;
    private readonly ConfigService _config;
    private readonly CityResolver _resolver;
    private readonly ILogger<OpenWeatherProvider> _log;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OpenWeatherProvider(IHttpClientFactory httpFactory, ConfigService config, CityResolver resolver, ILogger<OpenWeatherProvider> log)
    {
        _http = httpFactory.CreateClient();
        _config = config;
        _resolver = resolver;
        _log = log;
    }

    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        var key = _config.OpenWeather.Key;

        // 中文城市名 → 经纬度（OpenWeather 不支持裸中文 q，走地理编码）
        var coord = await _resolver.ResolveOpenWeatherAsync(cityName, ct)
            ?? throw new InvalidOperationException($"OpenWeather 城市解析失败：{cityName}");
        var parts = coord.Split(',');
        if (parts.Length != 2)
            throw new InvalidOperationException($"OpenWeather 坐标异常：{coord}");
        var param = $"lat={parts[0]}&lon={parts[1]}";

        // 实时 + 预报并行（3 小时步长）
        var nowTask = GetJsonAsync<OwWeatherResponse>($"{Base}/weather?{param}&appid={key}&units=metric&lang=zh_cn", ct);
        var fcTask = GetJsonAsync<OwForecastResponse>($"{Base}/forecast?{param}&appid={key}&units=metric&lang=zh_cn", ct);
        await Task.WhenAll(nowTask, fcTask);
        var now = nowTask.Result ?? throw new InvalidOperationException("OpenWeather 实时接口无数据");
        var fc = fcTask.Result ?? throw new InvalidOperationException("OpenWeather 预报接口无数据");

        // 坐标取数时 OpenWeather 返回的是网格内最近地点名（区县常显示为乡镇，如"下东乡"），
        // 以用户输入的城市名作为展示名更友好。
        var city = cityName;
        var timezone = TimeSpan.FromSeconds(now.Sys.Timezone);

        var weather = now.Weather.FirstOrDefault();
        var iconCode = ToQwIcon(weather?.Icon);

        var hourly = fc.List.Select(i => new ForecastHour
        {
            Time = UnixToLocal(i.Dt, timezone),
            Text = i.Weather.FirstOrDefault()?.Description ?? "",
            IconCode = ToQwIcon(i.Weather.FirstOrDefault()?.Icon),
            Temp = System.Math.Round((decimal)i.Main.Temp, 1)
        }).ToList();

        // 逐日：按本地日期聚合 3 小时片段
        var daily = hourly
            .GroupBy(h => h.Time.Date)
            .Select(g => new ForecastDay
            {
                Date = g.Key.Date,
                Text = g.FirstOrDefault(x => x.Time.Hour >= 12 && x.Time.Hour < 15)?.Text ?? g.First().Text,
                IconCode = g.FirstOrDefault(x => x.Time.Hour >= 12 && x.Time.Hour < 15)?.IconCode ?? g.First().IconCode,
                TempMin = g.Min(x => x.Temp),
                TempMax = g.Max(x => x.Temp)
            })
            .Take(5)
            .ToList();

        var result = new WeatherResult
        {
            Source = Name,
            FetchedAt = DateTime.Now,
            Now = new WeatherNow
            {
                City = city,
                LocationId = cityName,
                Temp = System.Math.Round((decimal)now.Main.Temp, 1),
                FeelsLike = System.Math.Round((decimal)now.Main.FeelsLike, 1),
                WeatherText = weather?.Description ?? "",
                IconCode = iconCode,
                WindDir = DegToDir(now.Wind.Deg),
                WindScale = WindScaleFromMs(now.Wind.Speed),
                Humidity = now.Main.Humidity,
                ObservedTime = UnixToLocal(now.Dt, timezone),
                Source = Name
            },
            Hourly = hourly,
            Daily = daily
        };

        _log.LogInformation("OpenWeather 取数成功：{city} {temp}°C {text}", city, result.Now.Temp, result.Now.WeatherText);
        return result;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var stream = await _http.GetStreamAsync(url, ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
    }

    private static DateTime UnixToLocal(long unix, TimeSpan tz) =>
        DateTimeOffset.FromUnixTimeSeconds(unix).ToOffset(tz).DateTime;

    /// <summary>风向角度 → 中文方位</summary>
    private static string DegToDir(int deg)
    {
        if (deg < 0) return "";
        var dirs = new[] { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
        return dirs[((deg + 22) % 360) / 45];
    }

    /// <summary>风速 m/s → 蒲福风力等级</summary>
    private static decimal WindScaleFromMs(double ms)
    {
        if (ms < 0.3) return 0;
        if (ms < 1.6) return 1;
        if (ms < 3.4) return 2;
        if (ms < 5.5) return 3;
        if (ms < 8.0) return 4;
        if (ms < 10.8) return 5;
        if (ms < 13.9) return 6;
        if (ms < 17.2) return 7;
        if (ms < 20.8) return 8;
        if (ms < 24.5) return 9;
        if (ms < 28.5) return 10;
        if (ms < 32.7) return 11;
        return 12;
    }

    // ------- OpenWeather icon code → 和风 icon code -------
    private static readonly Dictionary<string, string> IconMap = new()
    {
        ["01"] = "100",  // 晴
        ["02"] = "101",  // 少云
        ["03"] = "103",  // 晴间多云
        ["04"] = "104",  // 阴
        ["09"] = "300",  // 阵雨
        ["10"] = "305",  // 小雨
        ["11"] = "302",  // 雷阵雨
        ["13"] = "400",  // 雪
        ["50"] = "500",  // 雾
    };

    /// <summary>OW icon（"01d"/"13n"）→ 和风 3 位码；未知返回空（走 emoji 回退）</summary>
    private static string ToQwIcon(string? owIcon)
    {
        if (string.IsNullOrWhiteSpace(owIcon) || owIcon.Length < 2) return "";
        var key = owIcon.Substring(0, 2);
        return IconMap.TryGetValue(key, out var code) ? code : "";
    }
}