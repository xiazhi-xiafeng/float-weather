using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using FloatWeather.Models.Dto;
using FloatWeather.Models.Dto.Sources;
using FloatWeather.Services;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Providers;

/// <summary>
/// wttr.in（免费、无 Key）。把城市名直接交给 wttr.in 自身解析（自带中文地名库，覆盖区县），
/// 无需本地坐标解析，任何 Key 未配置也能用。
/// 天气文本来自 wttr.in 英文 weatherDesc，统一映射为和风 icon code 并渲染中文。
/// </summary>
public sealed class WttrInProvider : IWeatherProvider
{
    private const string Base = "https://wttr.in";

    public string Name => "wttr.in";

    // 免费源，无需 Key，始终可用
    public bool IsEnabled => true;

    private readonly HttpClient _http;
    private readonly ILogger<WttrInProvider> _log;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public WttrInProvider(IHttpClientFactory httpFactory, ILogger<WttrInProvider> log)
    {
        _http = httpFactory.CreateClient();
        _log = log;
    }

    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        // 地名直查：wttr.in 自身解析（免 Key、免本地坐标）/ format=j1 返回完整 JSON，m 强制公制
        using var response = await _http.GetAsync(
            $"{Base}/{Uri.EscapeDataString(cityName)}?format=j1&m",
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var data = await JsonSerializer.DeserializeAsync<WttrInResponse>(stream, JsonOpts, ct)
            ?? throw new InvalidOperationException("wttr.in 无数据");

        var cur = data.CurrentCondition.FirstOrDefault()
            ?? throw new InvalidOperationException("wttr.in 实时接口无数据");
        var (nowText, nowIcon) = MapText(cur.WeatherDesc);

        // 逐时：遍历每日的 hourly，拼日期 + HHMM
        var hourly = new List<ForecastHour>();
        foreach (var day in data.Weather)
        {
            var date = ParseDate(day.Date);
            foreach (var h in day.Hourly)
            {
                var (t, ic) = MapText(h.WeatherDesc);
                hourly.Add(new ForecastHour
                {
                    Time = date.AddHours(ParseHour(h.Time)),
                    Text = t,
                    IconCode = ic,
                    Temp = ParseD(h.TempC)
                });
            }
        }

        // 逐日：用整天天气码 → 文本；再取当天 12 点（无则首条）小时块作为天气
        var daily = new List<ForecastDay>();
        foreach (var day in data.Weather)
        {
            var noon = day.Hourly.FirstOrDefault(x => ParseHour(x.Time) == 12) ?? day.Hourly.FirstOrDefault();
            var (t, ic) = MapText(noon?.WeatherDesc);
            daily.Add(new ForecastDay
            {
                Date = ParseDate(day.Date),
                Text = t,
                IconCode = ic,
                TempMax = ParseD(day.MaxtempC),
                TempMin = ParseD(day.MintempC)
            });
        }

        var result = new WeatherResult
        {
            Source = Name,
            FetchedAt = DateTime.Now,
            Now = new WeatherNow
            {
                City = cityName,
                LocationId = cityName,
                Temp = ParseD(cur.TempC),
                FeelsLike = ParseD(cur.FeelsLikeC),
                WeatherText = nowText,
                IconCode = nowIcon,
                WindDir = CompassToDir(cur.Winddir16Point),
                WindScale = WindScaleFromKmph(ParseD(cur.WindspeedKmph)),
                Humidity = (int)ParseD(cur.Humidity),
                ObservedTime = ParseObserved(cur.LocalObsDateTime),
                Source = Name
            },
            Hourly = hourly.Take(48).ToList(),
            Daily = daily.Take(5).ToList()
        };

        _log.LogInformation("wttr.in 取数成功：{city} {temp}°C {text}", cityName, result.Now.Temp, result.Now.WeatherText);
        return result;
    }

    private static DateTime ParseObserved(string s)
    {
        // 形如 2026-08-25 13:00
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : default;
    }

    private static DateTime ParseDate(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : default;

    /// <summary>wttr.in 时间 HHMM（"300"/"1300"）→ 小时数</summary>
    private static int ParseHour(string s)
    {
        if (int.TryParse(s, out var v)) return v / 100;
        return 0;
    }

    private static decimal ParseD(string s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    /// <summary>风速 km/h → 蒲福风力等级</summary>
    private static decimal WindScaleFromKmph(decimal kmh) => WindScaleFromMs((double)(kmh / 3.6m));

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

    // ------- 16 方位英文缩写 → 中文 -------
    private static readonly Dictionary<string, string> CompassMap = new()
    {
        ["N"] = "北", ["NNE"] = "东北偏北", ["NE"] = "东北", ["ENE"] = "东北偏东",
        ["E"] = "东", ["ESE"] = "东南偏东", ["SE"] = "东南", ["SSE"] = "东南偏南",
        ["S"] = "南", ["SSW"] = "南偏西",   ["SW"] = "西南", ["WSW"] = "西南偏西",
        ["W"] = "西", ["WNW"] = "西北偏西", ["NW"] = "西北", ["NNW"] = "西北偏北",
    };

    private static string CompassToDir(string abbr)
    {
        if (string.IsNullOrWhiteSpace(abbr)) return "";
        return CompassMap.TryGetValue(abbr.Trim().ToUpperInvariant(), out var v) ? v : "";
    }

    // ------- 英文天气文本 → (中文, 和风 icon code) -------
    // 匹配顺序：具体在前、兜底在后。
    private static readonly (string Keyword, string Text, string Icon)[] TextMap =
    {
        ("hail",          "冰雹",   "304"),
        ("thunder",       "雷阵雨", "302"),
        ("sleet",         "雨夹雪", "404"),
        ("freezing",      "冻雨",   "404"),
        ("heavy snow",    "大雪",   "407"),
        ("moderate snow", "中雪",   "400"),
        ("light snow",    "小雪",   "400"),
        ("snow shower",   "阵雪",   "400"),
        ("blizzard",      "暴雪",   "407"),
        ("drifting snow", "雪",     "400"),
        ("snow",          "雪",     "400"),
        ("heavy rain",    "大雨",   "307"),
        ("moderate rain", "中雨",   "306"),
        ("light rain",    "小雨",   "305"),
        ("shower",        "阵雨",   "300"),
        ("patchy rain",   "阵雨",   "300"),
        ("rain",          "中雨",   "306"),
        ("drizzle",       "毛毛雨", "309"),
        ("overcast",      "阴",     "104"),
        ("fog",           "雾",     "500"),
        ("haze",          "霾",     "502"),
        ("mist",          "霾",     "502"),
        ("dust",          "沙尘暴", "503"),
        ("sand",          "沙尘",   "503"),
        ("partly cloudy", "晴间多云","103"),
        ("cloudy",        "少云",   "101"),
        ("clear",         "晴",     "100"),
        ("sunny",         "晴",     "100"),
    };

    private static (string Text, string Icon) MapText(List<WttrText>? desc)
    {
        var raw = desc?.FirstOrDefault()?.Value ?? "";
        var lower = raw.ToLowerInvariant();
        foreach (var (kw, t, ic) in TextMap)
            if (lower.Contains(kw)) return (t, ic);
        return (raw, "");
    }
}