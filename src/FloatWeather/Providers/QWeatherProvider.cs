using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FloatWeather.Models.Dto;
using FloatWeather.Models.Dto.Sources;
using FloatWeather.Services;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Providers;

/// <summary>
/// 和风天气 API v7 数据源（JWT/Ed25519 认证）。
/// 并行拉取：实时天气 / 24h 逐时 / 7d 逐日 / 空气质量 / 生活指数。
/// </summary>
public sealed class QWeatherProvider : IWeatherProvider
{
    public string Name => "和风天气";

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_config.QWeather.ProjectId) &&
        !string.IsNullOrWhiteSpace(_config.QWeather.CredentialId) &&
        !string.IsNullOrWhiteSpace(_config.QWeather.PrivateKey) &&
        !string.IsNullOrWhiteSpace(_config.QWeather.ApiHost) &&
        !_config.QWeather.ApiHost.Contains("你的APIHost");

    private readonly HttpClient _http;
    private readonly ConfigService _config;
    private readonly CityResolver _resolver;
    private readonly ILogger<QWeatherProvider> _log;

    public QWeatherProvider(IHttpClientFactory httpFactory, ConfigService config, CityResolver resolver, ILogger<QWeatherProvider> log)
    {
        _http = httpFactory.CreateClient("qweather");
        _config = config;
        _resolver = resolver;
        _log = log;
    }

    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        var host = _config.QWeather.ApiHost.TrimEnd('/');
        var token = QWeatherJwt.Build(_config.QWeather.ProjectId, _config.QWeather.CredentialId, _config.QWeather.PrivateKey);

        // 按城市名自动查询和风 LocationID
        var locationId = await _resolver.ResolveQWeatherAsync(cityName, ct)
            ?? throw new InvalidOperationException($"和风城市解析失败：{cityName}");
        var loc = Uri.EscapeDataString(locationId);

        string Url(string path, string extra = "") =>
            $"{host}/v7/{path}?location={loc}{(string.IsNullOrEmpty(extra) ? "" : "&" + extra)}";

        // 实时/逐时/逐日为核心接口，必须成功；空气质量与生活指数为辅助，失败仅静默降级为空（避免整源被弃用）
        var nowTask    = GetJsonAsync<QwNowResponse>(    Url("weather/now"),   token, ct);
        var hourlyTask = GetJsonAsync<QwHourlyResponse>( Url("weather/24h"),   token, ct);
        var dailyTask  = GetJsonAsync<QwDailyResponse>(  Url("weather/7d"),    token, ct);

        await Task.WhenAll(nowTask, dailyTask, hourlyTask);

        var now = nowTask.Result;
        var daily = dailyTask.Result;
        var hourly = hourlyTask.Result;

        EnsureOk(now?.Code, "now");
        EnsureOk(daily?.Code, "daily");
        EnsureOk(hourly?.Code, "hourly");

        // 辅助接口：任一个失败都只丢掉该辅助数据，主天气照常返回
        QwAirResponse? air = null;
        try
        {
            air = await GetJsonAsync<QwAirResponse>(Url("air/now"), token, ct);
            EnsureOk(air?.Code, "air");
        }
        catch (Exception ex)
        {
            _log.LogWarning("和风空气质量获取失败，已忽略：{msg}", ex.Message);
        }

        QwIndexResponse? indices = null;
        try
        {
            indices = await GetJsonAsync<QwIndexResponse>(Url("indices/1d", "type=0"), token, ct);
            EnsureOk(indices?.Code, "indices");
        }
        catch (Exception ex)
        {
            _log.LogWarning("和风生活指数获取失败，已忽略：{msg}", ex.Message);
        }

        // 显示实际解析的城市名（入参；区/县级地址在 Resolver 中已按末级区县抽取），而非固化配置里的 LocationId 城市
        var city = cityName;

        var result = new WeatherResult
        {
            Source = Name,
            FetchedAt = DateTime.Now,
            Now = new WeatherNow
            {
                City = city,
                LocationId = locationId,
                Temp = ParseD(now!.Now!.Temp),
                FeelsLike = ParseD(now.Now.FeelsLike),
                WeatherText = now.Now.Text,
                IconCode = now.Now.Icon,
                WindDir = now.Now.WindDir,
                WindScale = ParseD(now.Now.WindScale),
                Humidity = ParseI(now.Now.Humidity),
                ObservedTime = ParseTime(now.Now.ObsTime),
                Aqi = air?.Now != null ? ParseI(air.Now.Aqi) : 0,
                AqiCategory = air?.Now?.Category ?? "",
                AqiPrimary = air?.Now?.Primary ?? "",
                Source = Name
            },
            Hourly = (hourly?.Hourly ?? new()).Take(24).Select(h => new ForecastHour
            {
                Time = ParseTime(h.FxTime),
                Temp = ParseD(h.Temp),
                Text = h.Text,
                IconCode = h.Icon
            }).ToList(),
            Daily = (daily?.Daily ?? new()).Take(7).Select(d => new ForecastDay
            {
                Date = ParseTime(d.FxDate),
                TempMin = ParseD(d.TempMin),
                TempMax = ParseD(d.TempMax),
                Text = d.TextDay,
                IconCode = d.IconDay,
                WindDir = d.WindDirDay,
                WindScale = ParseD(d.WindScaleDay)
            }).ToList(),
            Indices = (indices?.Daily ?? new()).Select(x => new WeatherIndex
            {
                Name = x.Name,
                Level = string.IsNullOrWhiteSpace(x.Category) ? x.Level : x.Category,
                Text = x.Text
            }).ToList()
        };

        _log.LogInformation("和风天气取数成功：{city} {temp}°C {text}", city, result.Now.Temp, result.Now.WeatherText);
        return result;
    }

    private async Task<T?> GetJsonAsync<T>(string url, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, Json.Options, ct);
    }

    private static void EnsureOk(string? code, string name)
    {
        if (code != "200")
            throw new InvalidOperationException($"和风{name}接口返回异常 code={code}");
    }

    private static decimal ParseD(string s) => decimal.TryParse(s, out var v) ? v : 0m;
    private static int ParseI(string s) => int.TryParse(s, out var v) ? v : 0;
    private static DateTime ParseTime(string s) =>
        DateTimeOffset.TryParse(s, out var t) ? t.LocalDateTime : default;
}