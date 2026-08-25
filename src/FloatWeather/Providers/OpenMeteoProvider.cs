using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using FloatWeather.Models.Dto;
using FloatWeather.Models.Dto.Sources;
using FloatWeather.Services;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Providers;

/// <summary>
/// Open-Meteo（免费、无 Key）。按经纬度取实时 / 逐时 / 逐日。
/// 天气码为 WMO weather code，映射为和风 icon code 并渲染中文天气文本。
/// </summary>
public sealed class OpenMeteoProvider : IWeatherProvider
{
    private const string Base = "https://api.open-meteo.com/v1/forecast";

    public string Name => "Open-Meteo";

    // 免费源，无需 Key，始终可用
    public bool IsEnabled => true;

    private readonly HttpClient _http;
    private readonly CityResolver _resolver;
    private readonly ILogger<OpenMeteoProvider> _log;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OpenMeteoProvider(IHttpClientFactory httpFactory, CityResolver resolver, ILogger<OpenMeteoProvider> log)
    {
        _http = httpFactory.CreateClient();
        _resolver = resolver;
        _log = log;
    }

    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        var coord = await _resolver.ResolveCoordinateAsync(cityName, ct)
            ?? throw new InvalidOperationException($"Open-Meteo 城市解析失败：{cityName}");
        var parts = coord.Split(',');
        if (parts.Length != 2)
            throw new InvalidOperationException($"Open-Meteo 坐标异常：{coord}");

        var url = $"{Base}?latitude={parts[0]}&longitude={parts[1]}" +
                  "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
                  "&hourly=temperature_2m,weather_code" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                  "&timezone=auto&forecast_days=7&forecast_hours=48&wind_speed_unit=ms";

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var data = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOpts, ct)
            ?? throw new InvalidOperationException("Open-Meteo 无数据");

        var cur = data.Current ?? throw new InvalidOperationException("Open-Meteo 实时接口无数据");
        var (nowText, nowIcon) = WmoToText(cur.WeatherCode, cur.IsDay);

        var hourly = new List<ForecastHour>();
        var hTime = data.Hourly?.Time ?? new();
        var hTemp = data.Hourly?.Temperature2m ?? new();
        var hCode = data.Hourly?.WeatherCode ?? new();
        for (int i = 0; i < hTime.Count; i++)
        {
            var (t, ic) = WmoToText(hCode.ElementAtOrDefault(i));
            hourly.Add(new ForecastHour
            {
                Time = ParseIso(hTime[i]),
                Text = t,
                IconCode = ic,
                Temp = (decimal)(hTemp.ElementAtOrDefault(i) ?? 0)
            });
        }

        var daily = new List<ForecastDay>();
        var dTime = data.Daily?.Time ?? new();
        var dCode = data.Daily?.WeatherCode ?? new();
        var dMax = data.Daily?.Temperature2mMax ?? new();
        var dMin = data.Daily?.Temperature2mMin ?? new();
        for (int i = 0; i < dTime.Count; i++)
        {
            var (t, ic) = WmoToText(dCode.ElementAtOrDefault(i));
            daily.Add(new ForecastDay
            {
                Date = ParseIso(dTime[i]).Date,
                Text = t,
                IconCode = ic,
                TempMax = (decimal)(dMax.ElementAtOrDefault(i) ?? 0),
                TempMin = (decimal)(dMin.ElementAtOrDefault(i) ?? 0)
            });
        }

        var result = new WeatherResult
        {
            Source = Name,
            FetchedAt = DateTime.Now,
            Now = new WeatherNow
            {
                City = cityName,
                LocationId = coord,
                Temp = (decimal)cur.Temperature2m,
                FeelsLike = (decimal)cur.ApparentTemperature,
                WeatherText = nowText,
                IconCode = nowIcon,
                WindDir = DegToDir(cur.WindDirection10m),
                WindScale = WindScaleFromMs(cur.WindSpeed10m),
                Humidity = (int)Math.Round(cur.RelativeHumidity2m),
                ObservedTime = ParseIso(cur.Time),
                Source = Name
            },
            Hourly = hourly,
            Daily = daily.Take(5).ToList()
        };

        _log.LogInformation("Open-Meteo 取数成功：{city} {temp}°C {text}", cityName, result.Now.Temp, result.Now.WeatherText);
        return result;
    }

    private static DateTime ParseIso(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default;

    /// <summary>风向角度 → 中文方位</summary>
    private static string DegToDir(double deg)
    {
        if (deg < 0) return "";
        var dirs = new[] { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
        return dirs[((int)deg + 22) % 360 / 45];
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

    // ------- WMO weather code → (中文, 和风 icon code) -------
    private static readonly (int Code, string Text, string Icon)[] WmoMap =
    {
        (0,  "晴",     "100"),
        (1,  "少云",   "101"),
        (2,  "晴间多云","103"),
        (3,  "阴",     "104"),
        (45, "雾",     "500"),
        (48, "冻雾",   "500"),
        (51, "毛毛雨", "309"),
        (53, "毛毛雨", "309"),
        (55, "毛毛雨", "309"),
        (56, "冻毛毛雨","309"),
        (57, "冻毛毛雨","309"),
        (61, "小雨",   "305"),
        (63, "中雨",   "306"),
        (65, "大雨",   "307"),
        (66, "冻雨",   "404"),
        (67, "冻雨",   "404"),
        (71, "小雪",   "400"),
        (73, "中雪",   "400"),
        (75, "大雪",   "407"),
        (77, "雪粒",   "400"),
        (80, "阵雨",   "300"),
        (81, "强阵雨", "301"),
        (82, "疾风骤雨","302"),
        (85, "阵雪",   "400"),
        (86, "强阵雪", "400"),
        (95, "雷阵雨", "302"),
        (96, "雷阵雨伴冰雹","304"),
        (99, "雷阵雨伴冰雹","304"),
    };

    private static (string Text, string Icon) WmoToText(int? code, int isDay = 1)
    {
        foreach (var (c, t, ic) in WmoMap)
            if (c == code) return (t, ic);
        return ("未知", "");
    }
}