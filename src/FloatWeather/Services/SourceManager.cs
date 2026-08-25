using FloatWeather.Models.Dto;
using FloatWeather.Providers;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Services;

/// <summary>单个数据源的运行健康状态</summary>
public sealed class SourceHealth
{
    public string Name { get; init; } = "";
    public bool IsEnabled { get; set; }
    public int FailCount { get; set; }          // 连续失败次数（成功即清零）
    public bool IsOpen { get; set; }            // 熔断是否处于打开状态
    public DateTime? OpenUntil { get; set; }    // 熔断截止时间（到期自动恢复）
    public string LastError { get; set; } = "";
}

/// <summary>
/// 数据源调度核心：按优先级遍历可用源，支持超时、失败计数、熔断（冷却后自动恢复）与自动降级。
/// 返回首个成功结果；全部失败则抛出最后异常。
/// </summary>
public sealed class SourceManager
{
    private const int RequestTimeoutMs = 5000;     // 单次请求超时（含城市解析+取数），天气接口通常很快
    private const int FailureThreshold = 3;       // 连续失败达到该阈值进入熔断
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    private readonly List<IWeatherProvider> _providers;
    private readonly Dictionary<IWeatherProvider, SourceHealth> _health = new();
    private readonly ConfigService _config;
    private readonly ILogger<SourceManager> _log;

    public SourceManager(IEnumerable<IWeatherProvider> providers, ConfigService config, ILogger<SourceManager> log)
    {
        _log = log;
        _config = config;
        // 注册顺序作为默认顺序（和风 > 高德 > OpenWeather）
        _providers = providers.ToList();
        foreach (var p in _providers)
            _health[p] = new SourceHealth { Name = p.Name, IsEnabled = p.IsEnabled };
    }

    /// <summary>配置变更后刷新各源的 IsEnabled 状态</summary>
    public void RefreshEnabled()
    {
        foreach (var p in _providers)
            _health[p].IsEnabled = p.IsEnabled;
    }

    /// <summary>
    /// 返回尝试顺序：
    /// - 若未显式选择主/备，则按注册顺序返回全部源（默认全放开）。
    /// - 若选择了主/备，则「首选段」为主→备（剔重），其后接「兜底段」= 其余启用源按注册顺序。
    ///   未选中的源仅在首选段全部失败后才被使用。禁用源由 GetWeatherAsync 自行跳过。
    /// </summary>
    private List<IWeatherProvider> Ordered()
    {
        var primary = _config.Weather.PrimaryProvider;
        var fallback = _config.Weather.FallbackProvider;

        // 未做任何选择：保持注册顺序，所有启用源都可用
        if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(fallback))
            return new List<IWeatherProvider>(_providers);

        var selected = new List<IWeatherProvider>(2);
        var primaryP = _providers.FirstOrDefault(p => p.Name == primary);
        var fallbackP = _providers.FirstOrDefault(p => p.Name == fallback);
        if (primaryP is not null) selected.Add(primaryP);          // 主源必须排第一，与注册位置无关
        if (fallbackP is not null && fallbackP != primaryP) selected.Add(fallbackP);

        // 兜底段：注册顺序中未选中的源（只在首选全部失败后兜底）
        var ordered = new List<IWeatherProvider>(_providers.Count);
        ordered.AddRange(selected);
        foreach (var p in _providers)
        {
            if (!ordered.Contains(p)) ordered.Add(p);
        }
        return ordered;
    }

    /// <summary>各源健康快照（供设置页/调试展示）</summary>
    public IReadOnlyDictionary<string, SourceHealth> Health
    {
        get
        {
            lock (_health)
            {
                return _health.ToDictionary(kv => kv.Key.Name, kv => new SourceHealth
                {
                    Name = kv.Value.Name,
                    IsEnabled = kv.Value.IsEnabled,
                    FailCount = kv.Value.FailCount,
                    IsOpen = kv.Value.IsOpen,
                    OpenUntil = kv.Value.OpenUntil,
                    LastError = kv.Value.LastError
                });
            }
        }
    }

    /// <summary>
    /// 获取天气数据。按优先级尝试未熔断的可用源，失败降级到下一源。
    /// </summary>
    public async Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default)
    {
        Exception? last = null;
        var exhausted = true;

        foreach (var provider in Ordered())
        {
            if (!provider.IsEnabled)
                continue;
            exhausted = false;

            // 熔断检查：若处于打开状态且未到冷却结束，跳过该源
            lock (_health)
            {
                var h = _health[provider];
                if (h.IsOpen)
                {
                    if (h.OpenUntil is { } until && until <= DateTime.UtcNow)
                    {
                        h.IsOpen = false;   // 冷却到期，自动恢复
                        h.FailCount = 0;
                        _log.LogInformation("来源 {name} 熔断冷却结束，自动恢复", provider.Name);
                    }
                    else
                    {
                        continue;
                    }
                }
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeoutMs);
            try
            {
                var result = await provider.GetWeatherAsync(cityName, timeout.Token);
                MarkSuccess(provider);
                _log.LogInformation("数据来源: {provider}（{city} {temp}°C）",
                    provider.Name, result.Now?.City, result.Now?.Temp);
                return result;
            }
            catch (Exception ex)
            {
                last = ex;
                MarkFailure(provider, ex.Message);
                _log.LogWarning("来源 {provider} 获取失败，尝试降级到下一来源：{msg}",
                    provider.Name, ex.Message);
            }
        }

        if (exhausted)
            throw new InvalidOperationException("没有可用的天气数据源，请在 appsettings.json 中配置数据源 Key");
        throw last ?? new InvalidOperationException("所有天气数据源均失败");
    }

    private void MarkSuccess(IWeatherProvider provider)
    {
        lock (_health)
        {
            var h = _health[provider];
            h.FailCount = 0;
            h.LastError = "";
        }
    }

    private void MarkFailure(IWeatherProvider provider, string message)
    {
        lock (_health)
        {
            var h = _health[provider];
            h.FailCount++;
            h.LastError = message;
            if (!h.IsOpen && h.FailCount >= FailureThreshold)
            {
                h.IsOpen = true;
                h.OpenUntil = DateTime.UtcNow + Cooldown;
                _log.LogWarning("来源 {name} 连续失败 {n} 次，进入熔断 {s}s",
                    provider.Name, h.FailCount, Cooldown.TotalSeconds);
            }
        }
    }
}