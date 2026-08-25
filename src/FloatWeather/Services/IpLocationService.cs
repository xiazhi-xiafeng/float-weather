using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Services;

/// <summary>
/// 公网 IP 定位：当用户未设置城市时，通过免费接口获取当前网络所在城市。
/// 纯免 Key 多级 fallback（按顺序）：
///   myip.ipip.net/json → ip-api.com → 腾讯 r.inews → pconline（太平洋）。
/// 每级失败都静默降级到下一级，全部失败返回 null，由调用方兜底。
/// 若配置了高德 Key，则把高德 IP 定位作为最后的可选兜底（未配 Key 自动跳过）。
/// </summary>
public sealed class IpLocationService
{
    // 腾讯 / 太平洋接口返回 GBK 编码，需注册 CodePages 提供程序后才能按 GBK/GB18030 解码
    private static readonly Encoding Gbk = CreateGbk();

    private static Encoding CreateGbk()
    {
        // 注册 CodePages 提供程序后，Encoding.GetEncoding("gb18030") 才可用于解码 GBK 响应
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("gb18030");
    }

    private readonly IHttpClientFactory _factory;
    private readonly ConfigService _config;
    private readonly ILogger<IpLocationService> _log;

    public IpLocationService(IHttpClientFactory factory, ConfigService config, ILogger<IpLocationService> log)
    {
        _factory = factory;
        _config = config;
        _log = log;
    }

    /// <summary>依次尝试各免费接口，命中即返回城市名；全部失败返回 null。</summary>
    public async Task<string?> ResolveCityAsync(CancellationToken ct = default)
    {
        var city = NormalizeCity(await ViaIpipAsync(ct));       // 地级市源
        if (city is not null) return city;

        city = TrimCity(await ViaIpApiAsync(ct));               // 区县级源：保留原样，不补「市」
        if (city is not null) return city;

        city = NormalizeCity(await ViaTencentAsync(ct));        // 地级市源
        if (city is not null) return city;

        city = NormalizeCity(await ViaPconlineAsync(ct));       // 地级市源
        if (city is not null) return city;

        city = NormalizeCity(await ViaAmapAsync(ct));           // 地级市源
        if (city is not null) return city;

        _log.LogWarning("公网 IP 定位失败：所有免 Key 接口未返回城市");
        return null;
    }

    /// <summary>去除首尾空白；空返回 null。</summary>
    private static string? TrimCity(string? city)
        => string.IsNullOrWhiteSpace(city) ? null : city.Trim();

    /// <summary>市级源：名称不带任何行政单位后缀时补「市」（如 ipip 的"长沙"→"长沙市"）；已带市/区/县/旗/盟/州/新区等后缀则原样保留。</summary>
    private static string? NormalizeCity(string? city)
    {
        city = TrimCity(city);
        if (city is null) return null;
        if (city.EndsWith("市") || city.EndsWith("区") || city.EndsWith("县")
            || city.EndsWith("旗") || city.EndsWith("盟") || city.EndsWith("州")
            || city.EndsWith("新区")) return city;
        return city + "市";
    }

    /// <summary>myip.ipip.net/json：UTF-8 JSON，data.location 形如 ["中国","湖南","长沙","","电信"]。</summary>
    private async Task<string?> ViaIpipAsync(CancellationToken ct)
    {
        try
        {
            using var http = _factory.CreateClient();
            using var resp = await http.GetAsync("https://myip.ipip.net/json", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsByteArrayAsync(ct));
            if (doc.RootElement.GetPropertyValue("data", out var data)
                && data.GetPropertyValue("location", out var loc)
                && loc.ValueKind == JsonValueKind.Array && loc.GetArrayLength() >= 3)
            {
                var city = loc[2].GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(city)) return city;
            }
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "myip.ipip.net/json 定位失败");
            return null;
        }
    }

    /// <summary>ip-api.com：UTF-8 JSON，不传 IP 自动按请求来源定位，带 lang=zh-CN 返回中文。</summary>
    private async Task<string?> ViaIpApiAsync(CancellationToken ct)
    {
        try
        {
            using var http = _factory.CreateClient();
            const string url = "http://ip-api.com/json/?lang=zh-CN&fields=status,city";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsByteArrayAsync(ct));
            if (doc.RootElement.GetPropertyString("status") == "success")
            {
                var city = doc.RootElement.GetPropertyString("city");
                if (!string.IsNullOrWhiteSpace(city)) return city;
            }
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ip-api.com 定位失败");
            return null;
        }
    }

    /// <summary>腾讯 r.inews.qq.com/api/ip2city：JSON 但编码为 GBK，city 字段为城市名。</summary>
    private async Task<string?> ViaTencentAsync(CancellationToken ct)
    {
        try
        {
            using var http = _factory.CreateClient();
            using var resp = await http.GetAsync("https://r.inews.qq.com/api/ip2city", ct);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            using var doc = JsonDocument.Parse(Gbk.GetString(bytes));
            var city = doc.RootElement.GetPropertyString("city");
            return string.IsNullOrWhiteSpace(city) ? null : city;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "腾讯 r.inews 定位失败");
            return null;
        }
    }

    /// <summary>whois.pconline.com.cn/ipJson.jsp：GBK JSON，需带 Referer 否则 403，city 字段为城市名。</summary>
    private async Task<string?> ViaPconlineAsync(CancellationToken ct)
    {
        try
        {
            using var http = _factory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://whois.pconline.com.cn/ipJson.jsp?json=true");
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            req.Headers.Referrer = new Uri("https://www.pconline.com.cn/");
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var text = Gbk.GetString(bytes).Trim();
            using var doc = JsonDocument.Parse(text);
            var city = doc.RootElement.GetPropertyString("city");
            return string.IsNullOrWhiteSpace(city) ? null : city;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "pconline 定位失败");
            return null;
        }
    }

    /// <summary>高德 IP 定位：仅配置了高德 Key 时作为最后兜底（纯免 Key 场景通常不启用）。</summary>
    private async Task<string?> ViaAmapAsync(CancellationToken ct)
    {
        var key = _config.Amap.Key;
        if (string.IsNullOrWhiteSpace(key)) return null;
        try
        {
            using var http = _factory.CreateClient();
            using var resp = await http.GetAsync($"https://restapi.amap.com/v3/ip?key={key}", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsByteArrayAsync(ct));
            var city = doc.RootElement.GetPropertyString("city");
            if (!string.IsNullOrWhiteSpace(city)) return city;
            return doc.RootElement.GetPropertyString("province")?.Trim();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "高德 IP 定位失败");
            return null;
        }
    }
}

/// <summary>JsonElement 读取同名属性字符串 / 取值的小工具。</summary>
file static class JsonElementExtensions
{
    public static string? GetPropertyString(this JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()?.Trim()
            : null;

    public static bool GetPropertyValue(this JsonElement e, string name, out JsonElement value)
        => e.TryGetProperty(name, out value);
}