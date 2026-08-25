using System.Text.Json.Serialization;

namespace FloatWeather.Models.Dto.Sources;

/// <summary>wttr.in format=j1 响应</summary>
public sealed class WttrInResponse
{
    [JsonPropertyName("current_condition")] public List<WttrCurrent> CurrentCondition { get; set; } = new();
    public List<WttrDay> Weather { get; set; } = new();
}

/// <summary>实时条件</summary>
public sealed class WttrCurrent
{
    [JsonPropertyName("temp_C")] public string TempC { get; set; } = "";
    [JsonPropertyName("FeelsLikeC")] public string FeelsLikeC { get; set; } = "";
    public string Humidity { get; set; } = "";
    public string WeatherCode { get; set; } = "";
    public List<WttrText> WeatherDesc { get; set; } = new();

    public string Winddir16Point { get; set; } = "";
    public string WinddirDegree { get; set; } = "";
    public string WindspeedKmph { get; set; } = "";
    public string ObservationTime { get; set; } = "";
    public string LocalObsDateTime { get; set; } = "";
}

/// <summary>单日预报（含逐时）</summary>
public sealed class WttrDay
{
    public string Date { get; set; } = "";
    public string MaxtempC { get; set; } = "";
    public string MintempC { get; set; } = "";
    public List<WttrHourly> Hourly { get; set; } = new();
}

/// <summary>逐时预报</summary>
public sealed class WttrHourly
{
    /// <summary>24 小时制 HHMM：如 "300"（03:00）、"1300"（13:00）</summary>
    public string Time { get; set; } = "";
    public string TempC { get; set; } = "";
    public string FeelsLikeC { get; set; } = "";
    public string WeatherCode { get; set; } = "";
    public List<WttrText> WeatherDesc { get; set; } = new();
    public string Winddir16Point { get; set; } = "";
    public string WindspeedKmph { get; set; } = "";
}

/// <summary>天气文本（英文）</summary>
public sealed class WttrText
{
    public string Value { get; set; } = "";
}