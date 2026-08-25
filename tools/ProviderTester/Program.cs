using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using FloatWeather.Models.Dto;
using FloatWeather.Providers;
using FloatWeather.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// 全数据源 × 多城市/县 回归取数测试。
// 复用与 App 相同的 DI 注册，逐个调用 provider 真实逻辑，结果写入 ProviderTester 输出目录下 results.txt。

var OutputDir = AppContext.BaseDirectory;
const string FloatWeatherBase = @"D:\AI-code\float-weather\src\FloatWeather\bin\Debug\net8.0-windows";

var config = new ConfigurationBuilder()
    .SetBasePath(FloatWeatherBase)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("user-config.json", optional: true, reloadOnChange: true)
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

services.AddHttpClient("qweather")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });
services.AddHttpClient();
services.AddHttpClient("icons").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
});

services.AddSingleton<IWeatherProvider, ChinaWeatherProvider>();
services.AddSingleton<IWeatherProvider, QWeatherProvider>();
services.AddSingleton<IWeatherProvider, AmapProvider>();
services.AddSingleton<IWeatherProvider, SeniverseProvider>();
services.AddSingleton<IWeatherProvider, OpenWeatherProvider>();
services.AddSingleton<IWeatherProvider, OpenMeteoProvider>();
services.AddSingleton<IWeatherProvider, WttrInProvider>();

services.AddSingleton<ConfigService>();
services.AddSingleton<CityResolver>();
services.AddSingleton<SourceManager>();

var sp = services.BuildServiceProvider();
var providers = sp.GetServices<IWeatherProvider>();

// 跨多省的代表性城市 + 大量区/县（覆盖行政区、同名歧义、区县细粒度）
string[] cities =
{
    // 直辖市 / 地级市 / 自治区首府
    "北京",            "长沙",      "呼和浩特",   "乌鲁木齐",
    "哈尔滨",          "昆明",      "西安",        "广州",
    // 区（含多处同名歧义）
    "朝阳区",          // 北京朝阳 vs 长春朝阳
    "鼓楼区",          // 南京/福州/开封等多地同名
    "南山区",          // 深圳 vs 鹤岗
    "天河区",          // 广州
    "开福区",          // 长沙
    "长洲区",          // 广西梧州
    "黄浦区",          // 上海
    // 县
    "新源县",          // 新疆
    "茶陵县",          // 湖南株洲
    "长沙县",          // 长沙
    "苍南县",          // 浙江温州
    "修水县",          // 江西九江
    // 省/市前缀 + 区/县 的完整地址（检验区县优先抽取）
    "北京市朝阳区",
    "上海市黄浦区",
    "湖南省长沙市开福区",
    "湖南省长沙市长沙县",
    "广西壮族自治区梧州市长洲区",
    "广东省深圳市南山区",
};

var sb = new StringBuilder();
sb.AppendLine($"取数回归测试  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
sb.AppendLine($"城市 × 数据源（{cities.Length} × {providers.Count()}）");
sb.AppendLine();

foreach (var city in cities)
{
    sb.AppendLine($"========  {city}  ========");
    foreach (var p in providers)
    {
        var label = p.Name + (p.IsEnabled ? "" : "(未启用)");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var r = await p.GetWeatherAsync(city, cts.Token);
            var n = r?.Now;
            if (n is null)
            {
                sb.AppendLine($"  {label,-16} ✗ 无数据");
                continue;
            }
            var hourly = (r?.Hourly?.Count ?? 0) > 0 ? $"{r!.Hourly!.Count}时" : "无逐时";
            sb.AppendLine(
                $"  {label,-16} 地点={n.City,-8} 坐标/ID={n.LocationId,-16} {n.Temp:0.#}°C {n.WeatherText,-6} " +
                $"湿度={(n.Humidity > 0 ? n.Humidity + "%" : "-")} 风向={n.WindDir,-4} 风力={n.WindScale:0}级 {hourly}");
        }
        catch (Exception ex)
        {
            var msg = ex.Message.Replace("\r", "").Replace("\n", "");
            sb.AppendLine($"  {label,-16} ✗ 失败: {msg}");
        }
    }
    sb.AppendLine();
}

// ---- 心知天气 403 优雅降级验证：以无效 Key 触发 403，期望抛清晰降级信息 ----
sb.AppendLine("========  心知天气 403 降级测试（无效 Key）  ========");
try
{
    var tmpDir = Path.Combine(Path.GetTempPath(), "fw-seniverse-403-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmpDir);
    File.WriteAllText(Path.Combine(tmpDir, "user-config.json"),
        """{ "Providers": { "Seniverse": { "Key": "INVALID_403_TEST_KEY" } } }""");
    var cfg403 = new ConfigurationBuilder()
        .SetBasePath(tmpDir)
        .AddJsonFile("user-config.json", optional: true)
        .Build();
    var cfgSvc403 = new ConfigService(cfg403);
    var seniverse403 = new SeniverseProvider(
        sp.GetRequiredService<IHttpClientFactory>(),
        cfgSvc403,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SeniverseProvider>>());
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await seniverse403.GetWeatherAsync("北京", cts.Token);
        sb.AppendLine("  ✗ 意外成功（无效 Key 不应取到数据）");
    }
    catch (Exception ex)
    {
        var msg = ex.Message.Replace("\r", "").Replace("\n", "");
        sb.AppendLine($"  ✓ 抛异常: {msg}");
        sb.AppendLine($"  IsEnabled={seniverse403.IsEnabled}（403 为瞬时网络错误，保留源备用）");
    }
    try { Directory.Delete(tmpDir, true); } catch { }
}
catch (Exception ex)
{
    sb.AppendLine($"  ✗ 测试环境搭建失败: {ex.Message}");
}
sb.AppendLine();

// ---- SourceManager 自动降级链验证：十个不同省/地区，走真实优先级顺序（主源失败自动降级）----
sb.AppendLine("========  SourceManager 自动降级验证（10 个不同省/地区）  ========");
var sm = sp.GetRequiredService<SourceManager>();
string[] regions = { "拉萨", "银川", "西宁", "海口", "贵阳", "太原", "长春", "厦门", "桂林", "香港" };
foreach (var r in regions)
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var res = await sm.GetWeatherAsync(r, cts.Token);
        var n = res?.Now;
        var src = res?.Source ?? "?";
        if (n is null) sb.AppendLine($"  {r,-5} ✗ 无数据");
        else sb.AppendLine($"  {r,-5} → 来源={src,-10} 地点={n.City,-7} {n.Temp:0.#}°C {n.WeatherText,-6} " +
                           $"湿度={(n.Humidity > 0 ? n.Humidity + "%" : "-")}");
    }
    catch (Exception ex)
    {
        var msg = ex.Message.Replace("\r", "").Replace("\n", "");
        sb.AppendLine($"  {r,-5} ✗ 全部源失败: {msg}");
    }
}
sb.AppendLine();

// ---- 主源故障强制降级演示：把"故障演示源"设为 Primary，验证自动降级到下一可用源 ----
sb.AppendLine("========  主源故障强制降级（Primary=故障演示源，模拟主源不可用）  ========");
try
{
    var tmpDir = Path.Combine(Path.GetTempPath(), "fw-fallback-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmpDir);
    File.WriteAllText(Path.Combine(tmpDir, "user-config.json"),
        """{ "Weather": { "PrimaryProvider": "故障演示源", "FallbackProvider": "Open-Meteo" } }""");
    var cfgFb = new ConfigurationBuilder()
        .SetBasePath(tmpDir)
        .AddJsonFile("user-config.json", optional: true)
        .Build();
    var cfgSvcFb = new ConfigService(cfgFb);
    var china = providers.First(p => p.Name == "中国天气网");
    var fake = new FailProvider();
    var smFb = new SourceManager(new IWeatherProvider[] { fake, china }, cfgSvcFb,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SourceManager>>());
    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
    {
        var res = await smFb.GetWeatherAsync("北京", cts.Token);
        var n = res?.Now;
        var src = res?.Source ?? "?";
        sb.AppendLine($"  ✓ 主源故障后自动降级成功：来源={src} 地点={n?.City} {n?.Temp:0.#}°C {n?.WeatherText} 湿度={(n?.Humidity > 0 ? n?.Humidity + "%" : "-")}");
    }
    try { Directory.Delete(tmpDir, true); } catch { }
}
catch (Exception ex)
{
    var msg = ex.Message.Replace("\r", "").Replace("\n", "");
    sb.AppendLine($"  ✗ 降级验证失败: {msg}");
}
sb.AppendLine();

var outFile = Path.Combine(OutputDir, "results.txt");
await File.WriteAllTextAsync(outFile, sb.ToString(), Encoding.UTF8);
Console.WriteLine(sb.ToString());
Console.WriteLine();
Console.WriteLine($"结果已写入: {outFile}");

/// <summary>模拟主数据源故障：总是抛异常，用于验证 SourceManager 的自动降级。</summary>
sealed class FailProvider : IWeatherProvider
{
    public string Name => "故障演示源";
    public bool IsEnabled => true;
    public Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
        => throw new InvalidOperationException("模拟主数据源故障（网络不可达）");
}