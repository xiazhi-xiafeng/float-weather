using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FloatWeather.Models.Dto.Sources;
using FloatWeather.Providers;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Services;

/// <summary>
/// 城市解析器：按城市名自动查询各数据源的城市 ID，结果缓存。
/// 用户只需输入城市名（如"北京"），各源各自调城市查询 API 拿到自己的 ID。
/// </summary>
public sealed class CityResolver
{
    private readonly IHttpClientFactory _factory;
    private readonly ConfigService _config;
    private readonly ILogger<CityResolver> _log;

    /// <summary>缓存：城市名 → (解析分区, cityId)</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public CityResolver(IHttpClientFactory factory, ConfigService config, ILogger<CityResolver> log)
    {
        _factory = factory;
        _config = config;
        _log = log;
    }

    /// <summary>各数据源公开解析入口：带缓存、强类型，消除对源名字符串的耦合。</summary>
    public Task<string?> ResolveQWeatherAsync(string cityName, CancellationToken ct = default) =>
        ResolveCachedAsync(cityName, "qweather",
            (c, t) => ResolveDistrictFirstAsync(c, ResolveQWeatherCoreAsync, t), ct);

    public Task<string?> ResolveAmapAsync(string cityName, CancellationToken ct = default) =>
        ResolveCachedAsync(cityName, "amap", ResolveAmapCoreAsync, ct);

    public Task<string?> ResolveOpenWeatherAsync(string cityName, CancellationToken ct = default) =>
        ResolveCachedAsync(cityName, "openweather",
            (c, t) => ResolveDistrictFirstAsync(c, ResolveOpenWeatherCoreAsync, t), ct);

    /// <summary>坐标类数据源共用的解析链（Open-Meteo / wttr.in 等），命中同一缓存分区。</summary>
    public Task<string?> ResolveCoordinateAsync(string cityName, CancellationToken ct = default) =>
        ResolveCachedAsync(cityName, "coordinate", ResolveCoordinateCoreAsync, ct);

    /// <summary>带缓存与分区解析的统一入口：校验入参 → 命中缓存直返 → 解析成功后回写缓存。</summary>
    private async Task<string?> ResolveCachedAsync(
        string cityName, string partition,
        Func<string, CancellationToken, Task<string?>> resolver, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cityName)) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(cityName, out var byPart) && byPart.TryGetValue(partition, out var cached))
                return cached;
        }

        var result = await resolver(cityName, ct);
        if (result is not null)
        {
            lock (_lock)
            {
                if (!_cache.TryGetValue(cityName, out var byPart))
                {
                    byPart = new Dictionary<string, string>(StringComparer.Ordinal);
                    _cache[cityName] = byPart;
                }
                byPart[partition] = result;
            }
            _log.LogInformation("城市解析：{city} → {partition} ID={id}", cityName, partition, result);
        }
        return result;
    }

    /// <summary>
    /// 区县优先解析：对"xx市 xx区/县"类地址，先按末级区县名解析（可精确到区/县），失败再退回完整地址。
    /// 用于 OpenWeather / 和风 区县精确。
    /// </summary>
    private async Task<string?> ResolveDistrictFirstAsync(string cityName, Func<string, CancellationToken, Task<string?>> invoke, CancellationToken ct)
    {
        var district = ExtractDistrict(cityName);
        if (district is not null && district != cityName)
        {
            var r = await invoke(district, ct);
            if (r is not null) return r;
        }
        return await invoke(cityName, ct);
    }

    /// <summary>从"xx市 xx区/县"提取末级区县名（"长沙市开福区"→"开福区"；无"市"前缀则返回 null）。</summary>
    private static string? ExtractDistrict(string cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName)) return null;
        var idx = cityName.LastIndexOf('市');
        if (idx >= 0 && idx < cityName.Length - 1)
        {
            var tail = cityName[(idx + 1)..].Trim();
            if (tail.Length > 0) return tail;
        }
        return null;
    }

    /// <summary>和风 GeoAPI 城市查询（需 JWT 认证）</summary>
    private async Task<string?> ResolveQWeatherCoreAsync(string cityName, CancellationToken ct)
    {
        try
        {
            var host = _config.QWeather.ApiHost;
            var token = QWeatherJwt.Build(_config.QWeather.ProjectId, _config.QWeather.CredentialId, _config.QWeather.PrivateKey);
            using var http = _factory.CreateClient("qweather");
            var url = $"{host}/geo/v2/city/lookup?location={Uri.EscapeDataString(cityName)}&number=1";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("location", out var locArr) && locArr.GetArrayLength() > 0)
            {
                var first = locArr[0];
                if (first.TryGetProperty("id", out var idProp))
                    return idProp.GetString();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "和风城市查询失败：{city}", cityName);
        }
        return null;
    }

    /// <summary>高德地理编码 API 查 adcode</summary>
    private async Task<string?> ResolveAmapCoreAsync(string cityName, CancellationToken ct)
    {
        try
        {
            var key = _config.Amap.Key;
            if (string.IsNullOrWhiteSpace(key)) return null;
            using var http = _factory.CreateClient();
            var url = $"https://restapi.amap.com/v3/geocode/geo?key={key}&address={Uri.EscapeDataString(cityName)}";
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("geocodes", out var geoArr) && geoArr.GetArrayLength() > 0)
            {
                var first = geoArr[0];
                if (first.TryGetProperty("adcode", out var adProp))
                    return adProp.GetString();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "高德城市查询失败：{city}", cityName);
        }
        return null;
    }

    /// <summary>
/// OpenWeather 地理编码解析。"长洲"首条是香港长洲岛，故取 limit=5 后按启发式评分挑选（中文名匹配 &gt; 尾缀"区/县" &gt; 中国大陆）。
/// 针对"新源"这类裸县名（OpenWeather 对裸名常返回福建/江苏同名地），若入参不带"区/县"尾缀，追加一次 "{name}县" 查询合并打分，
/// 使"新源县"(新疆)这类行政区县命中率压过同名裸地（福建新源等）。
/// </summary>
private async Task<string?> ResolveOpenWeatherCoreAsync(string cityName, CancellationToken ct)
{
    try
    {
        var key = _config.OpenWeather.Key;
        if (string.IsNullOrWhiteSpace(key)) return null;
        using var http = _factory.CreateClient();

        // 裸名必查；末尾不带"区/县"再补一查 "{name}县"，覆盖裸县名歧义
        var queries = new List<string> { cityName };
        if (!cityName.EndsWith("县", StringComparison.Ordinal) &&
            !cityName.EndsWith("区", StringComparison.Ordinal))
            queries.Add(cityName + "县");

        string? bestCoords = null;
        var bestScore = -1;
        foreach (var q in queries)
        {
            var url = $"http://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(q)}&limit=5&appid={key}";
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var list = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (list.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in list.EnumerateArray())
            {
                var score = ScoreOpenWeatherResult(item, cityName);
                if (score <= bestScore) continue;
                if (item.TryGetProperty("lat", out var lat) && item.TryGetProperty("lon", out var lon))
                {
                    bestScore = score;
                    bestCoords = $"{lat.GetDouble():0.0000},{lon.GetDouble():0.0000}";
                }
            }
        }
        return bestCoords;
    }
    catch (Exception ex)
    {
        _log.LogWarning(ex, "OpenWeather 城市查询失败：{city}", cityName);
    }
    return null;
}

    /// <summary>对 OpenWeather geocode 单条结果打分，越高越可能匹配期望的行政区县。</summary>
    private int ScoreOpenWeatherResult(JsonElement item, string input)
    {
        var score = 0;
        var zh = GetPropertyOrEmpty(item, "local_names", "zh") + GetPropertyOrEmpty(item, "name", "");
        if (zh.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 10;                       // 中文名击中输入（"长洲"命中"长洲区"/"長洲"）
        if (zh.EndsWith("区") || zh.EndsWith("县"))
            score += 8;                        // 区县级名称更贴近用户意图
        var state = GetPropertyOrEmpty(item, "state", "");
        if (state is not ("" or "Hong Kong" or "Macau" or "Macao" or "Taiwan"))
            score += 5;                        // 中国大陆省份优先，避免港/澳/台同名地插队
        _log.LogDebug("OpenWeather 候选 {n}: zh={zh} state={state} 得分={score}", GetPropertyOrEmpty(item, "name", ""), zh, state, score);
        return score;
    }

    /// <summary>从对象上取（含嵌套）字符串属性，缺省返回 ""。路径上非对象节点（如字符串 local_names）直接返回 ""。</summary>
    private static string GetPropertyOrEmpty(JsonElement e, params string[] path)
    {
        var cur = e;
        foreach (var p in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out cur)) return "";
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() ?? "" : "";
    }

    /// <summary>对 Open-Meteo geocoding 单条结果打分，越高越可能匹配期望的行政区县（解决"长沙"等同名地歧义）。</summary>
    private int ScoreOpenMeteoResult(JsonElement item, string input)
    {
        var score = 0;
        var name = GetPropertyOrEmpty(item, "name");
        var admin2 = GetPropertyOrEmpty(item, "admin2");
        var admin1 = GetPropertyOrEmpty(item, "admin1");
        var cc = GetPropertyOrEmpty(item, "country_code");

        if (name.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 10;                              // 名称直接命中输入（"长沙"命中"长沙"）
        if ((name + admin2 + admin1).IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 6;                               // 上级行政区含输入（"长沙"命中"长沙市"）
        if (name.EndsWith("区") || name.EndsWith("县"))
            score += 8;                               // 区县级名称更贴近用户意图
        if (cc != "" && cc != "HK" && cc != "MO" && cc != "TW")
            score += 5;                               // 中国大陆优先，避免港/澳/台同名地插队

        // 主城(地级市/省会)加分：特征码 PPLC/PPLA/PPLA2 表示城市级中心，压过同名小镇(PPLA4 等)
        var fc = GetPropertyOrEmpty(item, "feature_code");
        if (fc is "PPLC" or "PPLA" or "PPLA2")
            score += 12;

        // 人口作主次 tiebreak：同名裸名时主城（人口最多）领先，压过同名小地
        if (item.TryGetProperty("population", out var pop) && pop.ValueKind == JsonValueKind.Number)
        {
            var p = pop.GetInt64();
            if (p > 0) score += (int)Math.Clamp(p / 200_000, 0, 20);   // ~每 20 万人 +1，封顶 +20
        }

        _log.LogDebug("Open-Meteo 候选 {n}: admin1={a1} admin2={a2} cc={cc} 得分={score}",
            name, admin1, admin2, cc, score);
        return score;
    }

    /// <summary>Open-Meteo 地理编码查 "lat,lon"（供 Open-Meteo 等免费源复用）。</summary>
    /// <remarks>
    /// 全程免 Key、不依赖任何已配置数据源：
    /// 1) Open-Meteo geocoder（免费）— 命中普通城市；
    /// 2) 命不中 "区/县" 级地名（如"开福区"）→ 借 wttr.in 地名直查拿坐标（免费、覆盖中国区县）；
    /// 3) 仍失败则仅在用户配置了高德 Key 时回退高德地理编码（可选增强，非必需）。
    /// </remarks>
    private async Task<string?> ResolveCoordinateCoreAsync(string cityName, CancellationToken ct)
    {
        try
        {
            // 裸名必查；末尾不带 市/区/县 再补查 "{name}市" 与 "{name}县"（覆盖"长沙"→湖南主城这类同名小镇歧义）。
            // 多条 query 的结果统一打分合并，选最优坐标，避免盲目取首条同名小镇。
            var queries = new List<string> { cityName };
            if (!cityName.EndsWith("市", StringComparison.Ordinal) &&
                !cityName.EndsWith("区", StringComparison.Ordinal) &&
                !cityName.EndsWith("县", StringComparison.Ordinal))
            {
                queries.Add(cityName + "市");
                queries.Add(cityName + "县");
            }

            string? best = null;
            var bestScore = -1;
            using var http = _factory.CreateClient();
            foreach (var q in queries)
            {
                var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(q)}&count=10&language=zh&format=json";
                using var resp = await http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (json.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    foreach (var item in results.EnumerateArray())
                    {
                        var score = ScoreOpenMeteoResult(item, cityName);
                        if (score <= bestScore) continue;
                        if (item.TryGetProperty("latitude", out var lat) && item.TryGetProperty("longitude", out var lon))
                        {
                            bestScore = score;
                            best = $"{lat.GetDouble():0.0000},{lon.GetDouble():0.0000}";
                        }
                    }
                }
            }
            if (best is not null) return best;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Open-Meteo 城市坐标查询失败：{city}", cityName);
        }

        // 二级：wttr.in 地名直查拿坐标（免费、免 Key，覆盖"区/县"）
        _log.LogInformation("Open-Meteo 未命中 {city}，回退 wttr.in 地名解析取坐标", cityName);
        var coord = await ResolveCoordinateViaWttrAsync(cityName, ct);
        if (coord is not null) return coord;

        // 三级（可选）：仅配置了高德 Key 时才回退高德地理编码，精确到经纬度
        if (!string.IsNullOrWhiteSpace(_config.Amap.Key))
        {
            _log.LogInformation("{city} 仍未命中，回退高德地理编码（已配置 Key）", cityName);
            return await ResolveCoordinateViaAmapAsync(cityName, ct);
        }
        return null;
    }

    /// <summary>借 wttr.in 地名直查拿坐标（免费、免 Key）。wttr j1 的 nearest_area[0] 含 latitude/longitude。</summary>
    private async Task<string?> ResolveCoordinateViaWttrAsync(string cityName, CancellationToken ct)
    {
        try
        {
            using var http = _factory.CreateClient();
            var url = $"https://wttr.in/{Uri.EscapeDataString(cityName)}?format=j1&m";
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("nearest_area", out var area) && area.GetArrayLength() > 0)
            {
                var first = area[0];
                if (first.TryGetProperty("latitude", out var lat) && first.TryGetProperty("longitude", out var lon))
                    return $"{lat.GetString()},{lon.GetString()}";
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "wttr.in 坐标解析失败：{city}", cityName);
        }
        return null;
    }

    /// <summary>高德地理编码取 "lat,lon"（高德 geocodes[].location 为 "lon,lat"）。依赖用户配置的高德 Key。</summary>
    private async Task<string?> ResolveCoordinateViaAmapAsync(string cityName, CancellationToken ct)
    {
        try
        {
            using var http = _factory.CreateClient();
            var url = $"https://restapi.amap.com/v3/geocode/geo?key={_config.Amap.Key}&address={Uri.EscapeDataString(cityName)}";
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("geocodes", out var geoArr) && geoArr.GetArrayLength() > 0)
            {
                var first = geoArr[0];
                if (first.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.String)
                {
                    var parts = loc.GetString()!.Split(',');
                    if (parts.Length == 2)
                        return $"{parts[1]},{parts[0]}";   // 高德 "lon,lat" → "lat,lon"
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "高德坐标解析失败：{city}", cityName);
        }
        return null;
    }
}
