using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace FloatWeather.Services;

/// <summary>和风天气配置（JWT 认证）</summary>
public sealed class QWeatherConfig
{
    /// <summary>项目ID（JWT 的 sub 声明）</summary>
    public string ProjectId { get; set; } = "";
    /// <summary>凭据ID（JWT 的 kid 声明）</summary>
    public string CredentialId { get; set; } = "";
    /// <summary>Ed25519 私钥（-----BEGIN PRIVATE KEY----- 整段）</summary>
    public string PrivateKey { get; set; } = "";
    /// <summary>开发者专属 API Host，形如 https://xxxxxx.re.qweatherapi.com</summary>
    public string ApiHost { get; set; } = "";
}

/// <summary>高德天气配置（Web 服务 Key）</summary>
public sealed class AmapConfig
{
    /// <summary>高德开放平台「Web服务」Key</summary>
    public string Key { get; set; } = "";
    /// <summary>行政区划码 adcode（如北京 110000）；留空则用 Weather.LocationId</summary>
    public string AdCode { get; set; } = "110000";
}

/// <summary>OpenWeather 配置（API Key）</summary>
public sealed class OpenWeatherConfig
{
    /// <summary>OpenWeather API Key</summary>
    public string Key { get; set; } = "";
}

/// <summary>心知天气配置（API Key）</summary>
public sealed class SeniverseConfig
{
    /// <summary>心知天气 API Key</summary>
    public string Key { get; set; } = "";
}

/// <summary>应用级配置</summary>
public sealed class WeatherAppConfig
{
    public int RefreshIntervalSeconds { get; set; } = 1800;
    public string CityName { get; set; } = "北京";
    public string LocationId { get; set; } = "101010100";

    /// <summary>主数据源名称（如"和风天气"/"高德天气"/"OpenWeather"）；留空则按注册顺序</summary>
    public string PrimaryProvider { get; set; } = "";

    /// <summary>备数据源名称；主源失败时依次降级到它</summary>
    public string FallbackProvider { get; set; } = "";
}

/// <summary>统一读取 appsettings.json 配置</summary>
public sealed class ConfigService
{
    private readonly IConfiguration _config;

    public ConfigService(IConfiguration config) => _config = config;

    public QWeatherConfig QWeather =>
        _config.GetSection("Providers:QWeather").Get<QWeatherConfig>() ?? new QWeatherConfig();

    public AmapConfig Amap =>
        _config.GetSection("Providers:Amap").Get<AmapConfig>() ?? new AmapConfig();

    public OpenWeatherConfig OpenWeather =>
        _config.GetSection("Providers:OpenWeather").Get<OpenWeatherConfig>() ?? new OpenWeatherConfig();

    public SeniverseConfig Seniverse =>
        _config.GetSection("Providers:Seniverse").Get<SeniverseConfig>() ?? new SeniverseConfig();

    public WeatherAppConfig Weather =>
        _config.GetSection("Weather").Get<WeatherAppConfig>() ?? new WeatherAppConfig();

    /// <summary>用户级配置覆盖文件的路径（位于应用目录，可写、不被构建覆盖）</summary>
    public static string UserConfigFile => Path.Combine(AppContext.BaseDirectory, "user-config.json");

    /// <summary>
    /// 保存 Weather 节到 user-config.json。已存在的其他节（Provider 等）保留合并。
    /// </summary>
    public void SaveWeather(WeatherAppConfig weather) => SaveSection("Weather", weather);

    /// <summary>用户是否已显式设置过城市（user-config.json 中存在非空 Weather.CityName）。内置默认"北京"不算用户设置。</summary>
    public bool IsCitySetFromUser()
    {
        try
        {
            if (!File.Exists(UserConfigFile)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(UserConfigFile));
            if (!doc.RootElement.TryGetProperty("Weather", out var w)) return false;
            if (!w.TryGetProperty("CityName", out var cn)) return false;
            return !string.IsNullOrWhiteSpace(cn.GetString());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>保存 Providers 节（含各数据源 Key 等敏感配置）到 user-config.json。</summary>
    public void SaveProviders(QWeatherConfig qw, AmapConfig amap, OpenWeatherConfig openWeather, SeniverseConfig seniverse)
    {
        var providers = new JsonObject
        {
            ["QWeather"] = JsonSerializer.SerializeToNode(qw),
            ["Amap"] = JsonSerializer.SerializeToNode(amap),
            ["OpenWeather"] = JsonSerializer.SerializeToNode(openWeather),
            ["Seniverse"] = JsonSerializer.SerializeToNode(seniverse),
        };
        SaveSection("Providers", providers);
    }

    private void SaveSection(string section, object value)
    {
        try
        {
            JsonObject root;
            if (File.Exists(UserConfigFile))
            {
                root = JsonNode.Parse(File.ReadAllText(UserConfigFile)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            root[section] = JsonSerializer.SerializeToNode(value);
            File.WriteAllText(UserConfigFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Config] 保存失败: " + ex.Message);
        }
    }
}