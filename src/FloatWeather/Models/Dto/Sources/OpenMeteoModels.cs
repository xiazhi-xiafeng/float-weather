using System.Text.Json.Serialization;

namespace FloatWeather.Models.Dto.Sources;

/// <summary>Open-Meteo forecast /v1/forecast 响应</summary>
public sealed class OpenMeteoResponse
{
    public float Latitude { get; set; }
    public float Longitude { get; set; }
    public string Timezone { get; set; } = "";

    public OpenMeteoCurrent? Current { get; set; }
    public OpenMeteoSeries? Hourly { get; set; }
    public OpenMeteoSeries? Daily { get; set; }
}

/// <summary>实时要素</summary>
public sealed class OpenMeteoCurrent
{
    public string Time { get; set; } = "";
    [JsonPropertyName("temperature_2m")] public double Temperature2m { get; set; }
    [JsonPropertyName("apparent_temperature")] public double ApparentTemperature { get; set; }
    [JsonPropertyName("relative_humidity_2m")] public double RelativeHumidity2m { get; set; }
    [JsonPropertyName("weather_code")] public int WeatherCode { get; set; }
    [JsonPropertyName("wind_speed_10m")] public double WindSpeed10m { get; set; }
    [JsonPropertyName("wind_direction_10m")] public double WindDirection10m { get; set; }
    [JsonPropertyName("is_day")] public int IsDay { get; set; } = 1;
}

/// <summary>逐时 / 逐日序列（共用一套首列字段）</summary>
public sealed class OpenMeteoSeries
{
    public List<string> Time { get; set; } = new();
    [JsonPropertyName("temperature_2m")] public List<double?> Temperature2m { get; set; } = new();
    [JsonPropertyName("weather_code")] public List<int?> WeatherCode { get; set; } = new();
    [JsonPropertyName("temperature_2m_max")] public List<double?> Temperature2mMax { get; set; } = new();
    [JsonPropertyName("temperature_2m_min")] public List<double?> Temperature2mMin { get; set; } = new();
}