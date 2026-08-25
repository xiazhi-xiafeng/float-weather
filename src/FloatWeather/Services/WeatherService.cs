using FloatWeather.Models.Dto;
using Microsoft.Extensions.Logging;

namespace FloatWeather.Services;

/// <summary>
/// 天气业务封装：调用 SourceManager 取数并缓存最近一次成功结果。
/// 断源时仍可返回最近缓存数据。
/// </summary>
public sealed class WeatherService
{
    private readonly SourceManager _sourceManager;
    private readonly ConfigService _config;
    private readonly ILogger<WeatherService> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _version; // 请求版本号：防止较旧请求的结果覆盖较新请求的快照

    public WeatherResult? Latest { get; private set; }
    public string LastError { get; private set; } = "";

    public WeatherService(SourceManager sourceManager, ConfigService config, ILogger<WeatherService> log)
    {
        _sourceManager = sourceManager;
        _config = config;
        _log = log;
    }

    /// <summary>刷新天气。失败保留上次缓存并返回该缓存。</summary>
    public async Task<WeatherResult?> RefreshAsync(CancellationToken ct = default)
    {
        var version = Interlocked.Increment(ref _version);
        await _lock.WaitAsync(ct);
        try
        {
            var result = await _sourceManager.GetWeatherAsync(_config.Weather.CityName, ct);

            // 若刷新期间已有更新版本的请求被发起，则本结果视为过期，交由最新请求决定最终快照
            if (version != Volatile.Read(ref _version))
                return Latest;

            Latest = result;
            LastError = "";
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log.LogError("天气获取失败: {msg}", ex.Message);
            return Latest; // 断源时返回上次缓存
        }
        finally
        {
            _lock.Release();
        }
    }
}