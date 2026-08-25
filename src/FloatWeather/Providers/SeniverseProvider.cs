using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FloatWeather.Models.Dto;
using FloatWeather.Models.Dto.Sources;
using FloatWeather.Services;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Providers;

/// <summary>
/// 心知天气（Seniverse）V3 API。实时 now + 逐时 hourly + 逐日 daily。
/// location 直接接受中文城市名，无需额外城市解析。
/// 天气文本为中文，映射为和风 icon code。
/// </summary>
public sealed class SeniverseProvider : IWeatherProvider
{
    private const string Base = "https://api.seniverse.com/v3/weather";

    public string Name => "心知天气";

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_config.Seniverse.Key) &&
        !_config.Seniverse.Key.Contains("你的心知Key");

    private readonly HttpClient _http;
    private readonly ConfigService _config;
    private readonly ILogger<SeniverseProvider> _log;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public SeniverseProvider(IHttpClientFactory httpFactory, ConfigService config, ILogger<SeniverseProvider> log)
    {
        _http = httpFactory.CreateClient();
        _config = config;
        _log = log;
    }

    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        var key = _config.Seniverse.Key;

        // 先解析城市：心知地点库仅支持市级名（长沙/北京），不支持"长沙市开福区"这类完整地址，
        // 故按"市"层级逐级降级，取首个可解析的短名；id 优先，避免二次解析。
        var (locStr, root) = await ResolveNowAsync(cityName, key, ct);
        var n = root.Now ?? throw new InvalidOperationException("心知实时接口无 now");
        var city = root.Location?.Name is { Length: > 0 } ? root.Location.Name : cityName;
        var (nowText, nowIcon) = ToQwIcon(n.Text);

        // daily + hourly 并行拉取。免费版无 hourly 权限(HTTP 403/AP010002)、daily 时机性 403 时，
        // 自动将对应列表降级为空，不中断主流程（实时 now 仍正常返回）。
        var hourlyTask = GetJsonAsync<SeniverseResponse>(WeatherUrl(locStr, "hourly", "&hours=24", key), ct, ignoreStatuses: new[] { 403 });
        var dailyTask = GetJsonAsync<SeniverseResponse>(WeatherUrl(locStr, "daily", "&start=0&days=5", key), ct, ignoreStatuses: new[] { 403 });
        await Task.WhenAll(hourlyTask, dailyTask);
        var hourResp = hourlyTask.Result;
        var dayResp = dailyTask.Result;

        var daily = new List<ForecastDay>();
        if (dayResp is not null)
        {
            foreach (var d in dayResp.Results.SelectMany(r => r.Daily))
            {
                var text = string.IsNullOrWhiteSpace(d.TextDay) ? d.TextNight : d.TextDay;
                var (t, ic) = ToQwIcon(text);
                daily.Add(new ForecastDay
                {
                    Date = ParseDate(d.Date),
                    Text = t,
                    IconCode = ic,
                    TempMax = ParseD(d.High),
                    TempMin = ParseD(d.Low),
                    WindDir = d.WindDirection,
                    WindScale = ParsePower(d.WindScale)
                });
            }
        }

        var hourly = new List<ForecastHour>();
        if (hourResp is not null)
        {
            foreach (var hs in hourResp.Results.SelectMany(r => r.Hourly))
            {
                var (t, ic) = ToQwIcon(hs.Text);
                hourly.Add(new ForecastHour
                {
                    Time = ParseTimestamp(hs.Time),
                    Text = t,
                    IconCode = ic,
                    Temp = ParseD(hs.Temperature)
                });
            }
        }

        var result = new WeatherResult
        {
            Source = Name,
            FetchedAt = DateTime.Now,
            Now = new WeatherNow
            {
                City = city,
                LocationId = locStr,
                Temp = ParseD(n.Temperature),
                FeelsLike = ParseD(n.FeelsLike),
                WeatherText = nowText,
                IconCode = nowIcon,
                WindDir = n.WindDirection is { Length: > 0 } ? n.WindDirection : "",
                WindScale = ParsePower(n.WindScale),
                Humidity = ParseDigits(n.Humidity),
                ObservedTime = ParseTimestamp(root.LastUpdate),
                Source = Name
            },
            Hourly = hourly.Take(24).ToList(),
            Daily = daily.Take(5).ToList()
        };

        _log.LogInformation("心知取数成功：{city} {temp}°C {text}", city, result.Now.Temp, result.Now.WeatherText);
        return result;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct, bool allowNotFound = false, IEnumerable<int>? ignoreStatuses = null)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        var status = (int)response.StatusCode;
        if (allowNotFound && status == 404)
            return default;   // 心知对未知 location 返回 404，此时交由候选名降级继续尝试
        if (ignoreStatuses?.Contains(status) == true)
            return default;   // 指定状态码（如无权限 403）不抛异常，调用方据此降级为空数据
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
    }

    /// <summary>构造天气接口 URL。location 已按市级名/id 传入，此处在拼接时编码。</summary>
    private static string WeatherUrl(string location, string type, string extras, string key) =>
        $"{Base}/{type}.json?key={Uri.EscapeDataString(key)}" +
        $"&location={Uri.EscapeDataString(location)}&language=zh-Hans&unit=c{extras}";

    /// <summary>拉取实况并按"市"层级降级解析城市，返回 (可用 location, 实况根节点)。</summary>
    private async Task<(string LocStr, SeniResult Root)> ResolveNowAsync(string cityName, string key, CancellationToken ct)
    {
        SeniResult? root = null;
        string resolved = cityName;
        var sawForbidden = false;   // 遇到过资源级 403（免费版区/县无权限），先记录，不中断候选
        foreach (var candidate in CityFallbacks(cityName))
        {
            SeniverseResponse? resp;
            try
            {
                resp = await GetJsonAsync<SeniverseResponse>(WeatherUrl(candidate, "now", "", key), ct, allowNotFound: true);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                // 403 多为"该区/县级别免费版无权限"，而非 Key 失效：跳过该候选，继续试更短的市级名
                // （如"北京市朝阳区"→403 →"北京市/北京"→可用）。真正的 Key 无效将导致所有候选都 403，
                // 此时走下方的 sawForbidden 全降级抛错。
                _log.LogDebug("心知候选 {city} 403(无权限)，尝试下一短名", candidate);
                sawForbidden = true;
                continue;
            }

            if (resp is { Results.Count: > 0 })
            {
                root = resp.Results[0];
                resolved = root.Location?.Id is { Length: > 0 } locId ? locId : candidate;
                break;
            }
            _log.LogDebug("心知找不到城市：{city}，尝试短名", candidate);
        }

        if (root is null)
        {
            // 全部候选均 403：连市级都用不了，判定 Key/账号无权限，抛清晰信息交由 SourceManager 降级到下一源
            if (sawForbidden)
                throw new InvalidOperationException("心知天气 Key 无效或无权限（403），已自动降级到其他数据源");
            throw new InvalidOperationException($"心知天气找不到城市：{cityName}");
        }
        return (resolved, root);
    }

    /// <summary>心知地点库仅支持市级名，生成逐级降级候选：
    /// 原样 → "…市"/去"市" → 剥省/自治区前缀后的末级市名（"广西…梧州市长洲区"→"梧州"/"梧州市"）；裸"区/县"去尾缀。</summary>
    private static IEnumerable<string> CityFallbacks(string city)
    {
        city = city.Trim();
        if (city.Length == 0) yield break;
        yield return city;

        var lastShi = city.LastIndexOf('市');
        if (lastShi > 0)
        {
            // 完整"…市区县"：先试"…市"整段，再去"市"
            var withShi = city[..(lastShi + 1)];   // "湖南省长沙市" / "广西…梧州市"
            yield return withShi;
            yield return withShi[..^1];             // "湖南省长沙" / "广西…梧州"
            // 剥省/自治区/州/盟等前缀，取末级市名，如"梧州"/"长沙"（免去前缀引起的不命中）
            var head = city[..lastShi];
            var bestEnd = -1;
            foreach (var m in new[] { "自治区", "自治州", "地区", "省", "盟" })
            {
                var i = head.IndexOf(m);
                while (i >= 0) { bestEnd = Math.Max(bestEnd, i + m.Length); i = head.IndexOf(m, i + m.Length); }
            }
            if (bestEnd >= 0)
            {
                var seg = head[bestEnd..];
                if (seg.Length > 0)
                {
                    yield return seg;               // "梧州" / "长沙"
                    yield return seg + "市";         // "梧州市" / "长沙市"
                }
            }
        }
        else if (city.EndsWith("区") || city.EndsWith("县") || city.EndsWith("市辖区"))
        {
            yield return city[..^1];                // 裸"朝阳区"→"朝阳"
        }
    }

    private static decimal ParseD(string s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    /// <summary>提取字符串中数字（如 "2级风力"→2、"60%"→60）</summary>
    private static int ParseDigits(string s)
    {
        var digits = new string(s?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
        return int.TryParse(digits, out var v) ? v : 0;
    }

    /// <summary>风力「X级风力」→ 数字等级</summary>
    private static decimal ParsePower(string s) => ParseDigits(s);

    private static DateTime ParseDate(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : default;

    /// <summary>形如 2026-08-25T13:00:00+08:00</summary>
    private static DateTime ParseTimestamp(string s) =>
        DateTimeOffset.TryParse(s, out var dto) ? dto.LocalDateTime : default;

    // ------- 心知天气文本 → 和风 icon code -------
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

    private static (string Text, string Icon) ToQwIcon(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("", "");
        foreach (var (keyword, code) in TextMap)
            if (text.Contains(keyword)) return (text, code);
        return (text, "");
    }
}