using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Json;
using FloatWeather.Models.Dto;
using FloatWeather.Models.Dto.Sources;
using FloatWeather.Services;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Providers;

/// <summary>
/// 高德天气 API（Web 服务 / v3）。
/// 支持实时 + 3 日预报；不提供逐时与生活指数，聚合结果中对应字段留空。
/// 图标码统一映射为和风 icon code，便于复用官方图标字体。
/// </summary>
public sealed class AmapProvider : IWeatherProvider
{
    private const string Endpoint = "https://restapi.amap.com/v3/weather/weatherInfo";

    public string Name => "高德天气";

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_config.Amap.Key) &&
        !_config.Amap.Key.Contains("你的高德Key");

    private readonly HttpClient _http;
    private readonly ConfigService _config;
    private readonly CityResolver _resolver;
    private readonly ILogger<AmapProvider> _log;

    public AmapProvider(IHttpClientFactory httpFactory, ConfigService config, CityResolver resolver, ILogger<AmapProvider> log)
    {
        _http = httpFactory.CreateClient();
        _config = config;
        _resolver = resolver;
        _log = log;
    }

    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        // 按城市名自动查询高德 adcode
        var adcode = await _resolver.ResolveAmapAsync(cityName, ct)
            ?? cityName; // 高德 city 参数也支持城市名，解析失败则直接用城市名兜底

        string Url(string extension) =>
            $"{Endpoint}?key={Uri.EscapeDataString(_config.Amap.Key)}&city={Uri.EscapeDataString(adcode)}&extensions={extension}";

        // 实时走 base，预报走 all，并行拉取
        var liveTask = GetJsonAsync<AmapResponse>(Url("base"), ct);
        var fcTask = GetJsonAsync<AmapResponse>(Url("all"), ct);

        await Task.WhenAll(liveTask, fcTask);

        var live = liveTask.Result;
        var fc = fcTask.Result;

        EnsureOk(live, "实时");
        EnsureOk(fc, "预报");

        var nowLive = live.Lives.FirstOrDefault()
            ?? throw new InvalidOperationException("高德实时接口无 lives 数据");
        var cast = fc.Forecasts.FirstOrDefault()?.Casts;

        var city = nowLive.City;

        var result = new WeatherResult
        {
            Source = Name,
            FetchedAt = DateTime.Now,
            Now = new WeatherNow
            {
                City = city,
                LocationId = adcode,
                Temp = ParseD(nowLive.Temperature),
                WeatherText = nowLive.Weather,
                IconCode = ToQwIcon(nowLive.Weather),
                WindDir = nowLive.Winddirection,
                WindScale = ParsePower(nowLive.Windpower),
                Humidity = ParseI(nowLive.Humidity),
                ObservedTime = ParseTime(nowLive.ReportTime),
                Source = Name
            },
            Daily = (cast ?? new()).Select(c => new ForecastDay
            {
                Date = ParseDate(c.Date),
                TempMin = ParseD(c.NightTemp),
                TempMax = ParseD(c.DayTemp),
                Text = string.IsNullOrWhiteSpace(c.DayWeather) ? c.NightWeather : c.DayWeather,
                IconCode = ToQwIcon(c.DayWeather),
                WindDir = c.DayWind,
                WindScale = ParsePower(c.DayPower)
            }).ToList()
        };

        _log.LogInformation("高德取数成功：{city} {temp}°C {text}", city, result.Now.Temp, result.Now.WeatherText);
        return result;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, Json.Options, ct);
    }

    private static void EnsureOk([NotNull] AmapResponse? resp, string tag)
    {
        if (resp is null || resp.Status != "1")
            throw new InvalidOperationException($"高德{tag}接口返回异常 status={resp?.Status} info={resp?.Info}");
    }

    private static decimal ParseD(string s) => decimal.TryParse(s, out var v) ? v : 0m;
    private static int ParseI(string s) => int.TryParse(s, out var v) ? v : 0;

    /// <summary>高德风力「X级」→ 数字等级</summary>
    private static decimal ParsePower(string s)
    {
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? 0m : decimal.Parse(digits);
    }

    private static DateTime ParseTime(string s) =>
        DateTimeOffset.TryParse(s, out var t) ? t.LocalDateTime : default;

    private static DateTime ParseDate(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : default;

    // ------- 高德天气文本 → 和风 icon code -------
    // 匹配顺序：具体在前、兜底在后。
    private static readonly (string Keyword, string Code)[] TextMap =
    {
        ("小雨",   "305"), ("阵雨",   "301"),
        ("雷雨",   "302"), ("暴雨",   "308"),
        ("大雨",   "307"), ("中雨",   "306"),
        ("冰雹",   "304"), ("雨夹雪", "404"),
        ("雪",     "400"),
        ("雨",     "305"),
        ("晴间多云","103"),("多云",   "101"),
        ("少云",   "102"),
        ("阴",     "104"), ("晴",     "100"),
        ("沙尘暴", "503"), ("霾",     "502"), ("雾", "500"),
        ("风",     "503"),
    };

    private static string ToQwIcon(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        foreach (var (keyword, code) in TextMap)
            if (text.Contains(keyword)) return code;
        return "";
    }
}