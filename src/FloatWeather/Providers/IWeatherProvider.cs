using FloatWeather.Models.Dto;

namespace FloatWeather.Providers;

/// <summary>
/// 天气数据源统一接口。每个数据源一个实现，便于主备切换与扩展。
/// </summary>
public interface IWeatherProvider
{
    /// <summary>来源显示名</summary>
    string Name { get; }

    /// <summary>是否已配置可用（如 Key 已填且有效）</summary>
    bool IsEnabled { get; }

    /// <summary>获取完整天气数据。</summary>
    /// <param name="cityName">城市名（如"北京"），各源内部自动解析为各自的城市 ID</param>
    /// <exception cref="Exception">取数失败时抛出，交由调度层降级</exception>
    Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default);
}