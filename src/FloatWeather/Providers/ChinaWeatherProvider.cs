using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FloatWeather.Models.Dto;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Providers;

/// <summary>
/// 中国天气网（中央气象台）数据源，免费、无需 Key。
/// 使用官方 JSON 接口：
///   - 城市ID解析：toy1.weather.com.cn/search  ；若精确匹配失败自动剥离省/市前缀取末级区县
///   - 数据聚合：d1.weather.com.cn/weather_index/{id}.html  （GBK 编码），内含
///       dataSK（实况 now）、fc（7 天逐日）、dataZS（生活指数）、cityDZ（今日）
///   - 逐小时：d1.weather.com.cn/wap_180h/{id}.html （返回 fc180.jh，约 7 天逐小时温度/天气）
/// </summary>
public sealed class ChinaWeatherProvider : IWeatherProvider
{
    public string Name => "中国天气网";

    // 免费源，无需 Key，始终可用
    public bool IsEnabled => true;

    private readonly HttpClient _http;
    private readonly ILogger<ChinaWeatherProvider> _log;
    private static readonly Regex NumberIn = new(@"\d+");

    public ChinaWeatherProvider(IHttpClientFactory httpFactory, ILogger<ChinaWeatherProvider> log)
    {
        _http = httpFactory.CreateClient();
        // 官网接口为 GBK 编码，需注册 CodePages 提供程序
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _log = log;
    }

    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        var cityId = await ResolveCityIdAsync(cityName, ct)
            ?? throw new InvalidOperationException($"中国天气网城市解析失败：{cityName}");

        var page = await GetPageAsync(cityId, ct);
        var dataSk = Extract(page, "dataSK");
        var fc = Extract(page, "fc");
        var dataZs = Extract(page, "dataZS");

        var city = GetString(dataSk, "cityname");
        if (string.IsNullOrWhiteSpace(city)) city = cityName;

        // 实况：温度 / 天气 / 风向风力 / 湿度 / AQI / 观测时间
        var tempTxt = GetString(dataSk, "temp");
        var temp = decimal.TryParse(tempTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var t0) ? t0 : 0m;
        var now = new WeatherNow
        {
            City = city,
            LocationId = cityId,
            Temp = temp,
            FeelsLike = temp,
            WeatherText = GetString(dataSk, "weather"),
            IconCode = WeatherCodeToIcon(GetString(dataSk, "weathercode")),
            WindDir = GetString(dataSk, "WD"),
            WindScale = WindScaleFromStr(GetString(dataSk, "WS")),
            Humidity = ParseInt(GetString(dataSk, "SD")),
            Aqi = ParseInt(GetString(dataSk, "aqi")),
            ObservedTime = ParseObsTime(page, tempTxt),
            Source = Name
        };

        var daily = ParseDaily(fc);
        var indices = ParseIndices(dataZs);
        var hourly = await GetHourlyAsync(cityId, ct);

        _log.LogInformation("中国天气网取数成功:{city} {temp}°C {text} ({id})", city, now.Temp, now.WeatherText, cityId);
        return new WeatherResult
        {
            Source = Name,
            FetchedAt = DateTime.Now,
            Now = now,
            Hourly = hourly,
            Daily = daily,
            Indices = indices
        };
    }

    // ---------------- 城市ID解析 ----------------
    private async Task<string?> ResolveCityIdAsync(string cityName, CancellationToken ct)
    {
        string? bestId = null;
        var bestScore = -1;
        try
        {
            // 生成查询变体：原词 → 末级名 → 剥离行政区后缀的核心名。
            // toy1 对"XX区"形式的支持很差（常只命中"XX区办事处"街道级），故必须用核心名（去掉"区/县/市"）再搜。
            var trimmed = cityName.Trim();
            var terminal = LastSegment(trimmed);
            var queries = new List<string>();
            foreach (var s in new[] { trimmed, terminal })
            {
                if (s.Length > 0 && !queries.Contains(s)) queries.Add(s);
                var core = StripSuffix(s);
                if (core.Length > 0 && !queries.Contains(core)) queries.Add(core);
            }

            foreach (var q in queries)
            {
                var url = "http://toy1.weather.com.cn/search?cityname=" + Uri.EscapeDataString(q);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                // 搜索接口需带 Referer 与桌面 UA 才会返回结果，否则返回空括号
                req.Headers.Referrer = new Uri("http://www.weather.com.cn/");
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
                using var resp = await _http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                // 搜索接口实测为 UTF-8 编码（不用 GBK），否则中文城市名乱码导致匹配失败
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                var txt = DecodeBytes(bytes);

                // 返回形如 ( [ {...}, {...} ] ) 的 JSONP
                var s = txt;
                var open = s.IndexOf('[');
                var close = s.LastIndexOf(']');
                if (open < 0 || close <= open) continue;

                using var doc = JsonDocument.Parse(s.Substring(open, close - open + 1));
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("ref", out var rf) || rf.GetString() is not { } refStr)
                        continue;
                    var parts = refStr.Split('~');
                    if (parts.Length < 3) continue;
                    var id = parts[0];
                    var name = parts[2];

                    // 跳过非行政区（景点/场馆，ID 含字母，如 ...01A）
                    if (id.Any(char.IsLetter)) continue;
                    var nameen = parts.Length > 1 ? parts[1] : "";

                    var score = ScoreCity(id, name, q);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestId = id;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "中国天气网城市ID解析失败:{city}", cityName);
        }
        return bestScore > 0 ? bestId : null;
    }

    private static string LastSegment(string city)
    {
        city = city.Trim();
        // 剥直辖市/省市前缀，取末级区县市名（"北京市朝阳区"→"朝阳区"、"湖南省长沙市"→"长沙市"）
        var idx = -1;
        foreach (var m in new[] { "自治区", "自治州", "地区", "省", "市", "特别行政区" })
        {
            var i = city.LastIndexOf(m, StringComparison.Ordinal);
            while (i >= 0)
            {
                idx = Math.Max(idx, Math.Min(city.Length - 1, i + m.Length));
                i = city.LastIndexOf(m, i - 1, StringComparison.Ordinal);
            }
        }
        if (idx <= 0 || idx >= city.Length) return city;
        return city[idx..].TrimStart('市').Trim();
    }

    /// <summary>剥离行政区尾缀，得到核心名（"朝阳区"→"朝阳"、"长沙县"→"长沙"）。</summary>
    private static string StripSuffix(string s)
    {
        s = s.Trim();
        foreach (var suf in new[] { "特别行政区", "自治州", "自治区", "自治县", "地区", "县", "区", "市" })
        {
            if (s.EndsWith(suf, StringComparison.Ordinal) && s.Length > suf.Length)
                return s[..^suf.Length];
        }
        return s;
    }

    /// <summary>
    /// 对搜索结果条目评分。核心策略：
    ///  1) 用"去后缀核心名"匹配，规避 toy1 对"XX区"只命中街道级的问题；
    ///  2) 9 位市/区/县代码优先（weather_index 数据接口仅对这类 ID 返回完整数据），12 位街道/镇级压低。
    /// </summary>
    private static int ScoreCity(string id, string name, string query)
    {
        var nBase = StripSuffix(name);
        var qBase = StripSuffix(query);
        var nine = id.Length == 9;

        // 命中判定：全名相等，或核心名相等，或名称包含查询核心（"朝阳区办事处"匹配"朝阳"）
        var match = name == query
            || (qBase.Length > 0 && nBase == qBase)
            || (qBase.Length > 0 && name.Contains(qBase, StringComparison.Ordinal));
        if (!match) return 0;

        var score = 30;                       // 有效行政区
        if (name == query) score += 40;       // 精确全名
        else if (qBase.Length > 0 && nBase == qBase) score += 30; // 核心名一致
        else score += 10;

        if (nine) score += 35;                // 9位行政代码数据完整
        else score -= 10;                     // 街道/镇级，数据接口常无天气数，压低

        // 区/县/市为更常见的粒度，轻微加分
        if (name.EndsWith("区") || name.EndsWith("县")) score += 5;
        if (name.EndsWith("市")) score += 5;
        return score;
    }

    // ---------------- 数据拉取与解析 ----------------
    private async Task<string> GetPageAsync(string cityId, CancellationToken ct)
    {
        var url = "http://d1.weather.com.cn/weather_index/" + cityId + ".html";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // 官方接口需带 Referer 与桌面 UA 才能放行
        request.Headers.Referrer = new Uri("http://www.weather.com.cn/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");

        using var resp = await _http.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return DecodeBytes(bytes);
    }

    /// <summary>接口编码实测为 UTF-8；官方文档虽标注 GBK，但为防服务器切换回退，自动探测（先用 UTF-8 严格解码，失败则用 GBK）。</summary>
    private static string DecodeBytes(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("GBK").GetString(bytes);
        }
    }

    // ---------------- 逐小时预报（wap_180h） ----------------
    /// <summary>
    /// 取数接口：d1.weather.com.cn/wap_180h/{id}.html，返回 var fc180={...}，其 jh 数组为逐小时序列
    /// （jf=yyyyMMddHHmm 时间、ja=天气码、jb=温度、jc=风级、jd=风向、je=湿度）。
    /// 需带 Referer 与移动 UA，否则返回空。
    /// </summary>
    private async Task<List<ForecastHour>> GetHourlyAsync(string cityId, CancellationToken ct)
    {
        try
        {
            var url = "https://d1.weather.com.cn/wap_180h/" + cityId + ".html";
            string raw;
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.Referrer = new Uri("https://m.weather.com.cn/");
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148");
                using var resp = await _http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                raw = DecodeBytes(bytes);
            }

            if (Extract(raw, "fc180") is not { } fc180
                || !fc180.TryGetProperty("jh", out var jhHttp))
                return new();

            var list = new List<ForecastHour>();
            foreach (var h in jhHttp.EnumerateArray())
            {
                var t = GetString(h, "jf");          // 202608251200
                if (t.Length < 12) continue;
                if (!DateTime.TryParseExact(t, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    continue;
                var (text, icon) = WeatherCodeTo(GetString(h, "ja"));
                list.Add(new ForecastHour
                {
                    Time = time,
                    Temp = ParseMoney(GetString(h, "jb")),
                    Text = text,
                    IconCode = icon
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "中国天气网逐小时取数失败:{cityId}", cityId);
            return new();
        }
    }

    /// <summary>从"var name = {...};"文本块中抽取平衡花括号的 JSON 对象并返回 JsonElement。</summary>
    private JsonElement? Extract(string page, string name)
    {
        // weather_index 返回 "var name = {...}"（带空格）；wap_180h 返回 "var name={...}"（无空格）
        var needle = "var " + name + " =";
        var i = page.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0)
        {
            i = page.IndexOf("var " + name + "=", StringComparison.Ordinal);
            if (i >= 0) i = i + ("var " + name).Length; // 指向 '='
        }
        if (i < 0) return null;
        var start = page.IndexOf('{', i);
        if (start < 0) return null;

        var depth = 0;
        var j = start;
        for (; j < page.Length; j++)
        {
            if (page[j] == '{') depth++;
            else if (page[j] == '}')
            {
                depth--;
                if (depth == 0) { j++; break; }
            }
        }
        if (depth != 0) return null;
        var json = page.Substring(start, j - start);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static List<ForecastDay> ParseDaily(JsonElement? fc)
    {
        var list = new List<ForecastDay>();
        if (fc is not { } root || !root.TryGetProperty("f", out var arr))
            return list;

        foreach (var d in arr.EnumerateArray())
        {
            var fa = GetString(d, "fa");   // 白天天气码
            var fcMax = GetString(d, "fc"); // 最高温
            var fdMin = GetString(d, "fd"); // 最低温
            var (text, icon) = WeatherCodeTo(fa);

            list.Add(new ForecastDay
            {
                Date = ParseDate(GetString(d, "fi")),
                IconCode = icon,
                Text = text,
                TempMin = ParseMoney(fdMin),
                TempMax = ParseMoney(fcMax),
                WindDir = GetString(d, "fe"),
                WindScale = WindScaleFromStr(GetString(d, "fg")),
            });
        }
        return list.Take(7).ToList();
    }

    private static List<WeatherIndex> ParseIndices(JsonElement? dataZs)
    {
        var list = new List<WeatherIndex>();
        if (dataZs is not { } root || !root.TryGetProperty("zs", out var zs))
            return list;

        foreach (var prop in zs.EnumerateObject())
        {
            if (!prop.Name.EndsWith("_name", StringComparison.Ordinal)) continue;
            var prefix = prop.Name[..^"_name".Length];
            var name = prop.Value.GetString() ?? "";

            var level = "";
            if (zs.TryGetProperty(prefix + "_hint", out var hint)) level = hint.GetString() ?? "";
            var text = "";
            if (zs.TryGetProperty(prefix + "_des_s", out var des)) text = des.GetString() ?? "";

            if (name.Length == 0) continue;
            list.Add(new WeatherIndex { Name = name, Level = level, Text = text });
        }
        return list;
    }

    // ---------------- 工具方法 ----------------
    private static string GetString(JsonElement? el, string key)
    {
        if (el is not { } root)
            return "";
        if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    private static decimal ParseMoney(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        var m = NumberIn.Match(s);
        return m.Success && decimal.TryParse(m.Value, out var v) ? v : 0m;
    }

    private static int ParseInt(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var m = NumberIn.Match(s);
        return m.Success && int.TryParse(m.Value, out var v) ? v : 0;
    }

    /// <summary>从"3级"/"&lt;3级"风级字符串提取风力等级数字。</summary>
    private static decimal WindScaleFromStr(string s)
    {
        var m = NumberIn.Match(s ?? "");
        return m.Success && decimal.TryParse(m.Value, out var v) ? v : 0m;
    }

    /// <summary>解析实况观测时间：dataSK 无完整日期，用今天的日期拼装 &quot;HH:mm&quot;。</summary>
    private static DateTime ParseObsTime(string page, string tempTxt)
    {
        // 实况块里 time 形如 "14:05"
        var m = new Regex(@"""time"":""(?<t>\d{1,2}:\d{2})""").Match(page);
        if (m.Success && DateTime.TryParseExact(m.Groups["t"].Value, "H:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var tm))
            return DateTime.Now.Date.Add(tm.TimeOfDay);
        return DateTime.Now;
    }

    private static DateTime ParseDate(string fi)
        => DateTime.TryParseExact(fi, "M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? new DateTime(DateTime.Now.Year, d.Month, d.Day)
            : default;

    // ---------------- 中国天气码 → (中文天气, 和风 icon code) ----------------
    private static (string Text, string Icon) WeatherCodeTo(string code)
    {
        if (string.IsNullOrEmpty(code)) return ("未知", "");
        // 实时块天气码带 d/n 前缀，如 "d02"
        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var n)) return ("未知", "");
        // 夜间码 50+（50=晴夜...）映射回 00.. 语义
        if (n >= 50) n -= 50;
        return Map.TryGetValue(n, out var v) ? v : ("未知", "");
    }

    private static string WeatherCodeToIcon(string code) => WeatherCodeTo(code).Icon;

    private static readonly Dictionary<int, (string Text, string Icon)> Map = new()
    {
        [0]  = ("晴",       "100"),
        [1]  = ("多云",     "101"),
        [2]  = ("阴",       "104"),
        [3]  = ("阵雨",     "300"),
        [4]  = ("雷阵雨",   "302"),
        [5]  = ("雷阵雨伴有冰雹", "304"),
        [6]  = ("雨夹雪",   "401"),
        [7]  = ("小雨",     "305"),
        [8]  = ("中雨",     "306"),
        [9]  = ("大雨",     "307"),
        [10] = ("暴雨",     "308"),
        [11] = ("大暴雨",   "309"),
        [12] = ("特大暴雨", "310"),
        [13] = ("阵雪",     "400"),
        [14] = ("小雪",     "400"),
        [15] = ("中雪",     "401"),
        [16] = ("大雪",     "402"),
        [17] = ("暴雪",     "403"),
        [18] = ("雾",       "500"),
        [19] = ("冻雨",     "404"),
        [20] = ("沙尘暴",   "504"),
        [21] = ("小到中雨", "305"),
        [22] = ("中到大雨", "306"),
        [23] = ("大到暴雨", "308"),
        [24] = ("暴雨到大暴雨", "309"),
        [25] = ("大暴雨到特大暴雨", "310"),
        [26] = ("小到中雪", "400"),
        [27] = ("中到大雪", "401"),
        [28] = ("大到暴雪", "402"),
        [29] = ("浮尘",     "502"),
        [30] = ("扬沙",     "504"),
        [31] = ("强沙尘暴", "505"),
        [32] = ("霾",       "503"),
        [33] = ("中度霾",   "503"),
        [34] = ("重度霾",   "503"),
        [35] = ("严重霾",   "503"),
        [36] = ("大雾",     "500"),
        [37] = ("特强浓雾", "500"),
        [38] = ("极重度霾", "503"),
        [49] = ("阵雨",     "300"),
    };
}