namespace FloatWeather.Models.Dto;

using System.Text.Json.Serialization;
using FontFamily = System.Windows.Media.FontFamily;

/// <summary>统一实时天气</summary>
public sealed class WeatherNow
{
    public string City { get; set; } = "";
    public string LocationId { get; set; } = "";
    public decimal Temp { get; set; }               // °C
    public decimal FeelsLike { get; set; }
    public string WeatherText { get; set; } = "";   // 多云/晴...
    public string IconCode { get; set; } = "";      // 源图标码
    public string WindDir { get; set; } = "";       // 风向
    public decimal WindScale { get; set; }          // 风力等级
    public int Humidity { get; set; }               // %
    public int Aqi { get; set; }                    // 空气质量指数
    public string AqiCategory { get; set; } = "";   // 优/良/轻度污染...
    public string AqiPrimary { get; set; } = "";    // 首要污染物
    public DateTime ObservedTime { get; set; }      // 观测时间
    public string Source { get; set; } = "";        // 数据来源标识
}

/// <summary>统一逐时预报</summary>
public sealed class ForecastHour
{
    public DateTime Time { get; set; }
    public string IconCode { get; set; } = "";
    public string Text { get; set; } = "";
    public decimal Temp { get; set; }

    /// <summary>展示用：天气字形（由 ViewModel 填充，不参与序列化）</summary>
    [JsonIgnore]
    public string IconText { get; set; } = "";
    [JsonIgnore]
    public FontFamily? IconFont { get; set; }
}

/// <summary>统一逐日预报</summary>
public sealed class ForecastDay
{
    public DateTime Date { get; set; }
    public string IconCode { get; set; } = "";
    public string Text { get; set; } = "";
    public decimal TempMin { get; set; }
    public decimal TempMax { get; set; }
    public string WindDir { get; set; } = "";
    public decimal WindScale { get; set; }

    /// <summary>展示用：天气字形（由 ViewModel 填充，不参与序列化）</summary>
    [JsonIgnore]
    public string IconText { get; set; } = "";
    [JsonIgnore]
    public FontFamily? IconFont { get; set; }
}

/// <summary>统一生活指数</summary>
public sealed class WeatherIndex
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>聚合天气结果（一次请求的完整载荷）</summary>
public sealed class WeatherResult
{
    public WeatherNow? Now { get; set; }
    public List<ForecastHour> Hourly { get; set; } = new();
    public List<ForecastDay> Daily { get; set; } = new();
    public List<WeatherIndex> Indices { get; set; } = new();
    public string Source { get; set; } = "";
    public DateTime FetchedAt { get; set; } = DateTime.Now;
}