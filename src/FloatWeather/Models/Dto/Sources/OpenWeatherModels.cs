using System.Text.Json.Serialization;

namespace FloatWeather.Models.Dto.Sources;

// OpenWeather 天气接口（api.openweathermap.org/data/2.5）
// 实时 weather + 预报 forecast。字段为 snake_case，需映射。

/// <summary>实时天气响应（/weather）</summary>
public class OwWeatherResponse
{
    [JsonPropertyName("weather")] public List<OwWeather> Weather { get; set; } = new();
    [JsonPropertyName("main")] public OwMainInfo Main { get; set; } = new();
    [JsonPropertyName("wind")] public OwWindInfo Wind { get; set; } = new();
    [JsonPropertyName("clouds")] public OwCloudsInfo Clouds { get; set; } = new();
    [JsonPropertyName("sys")] public OwSysInfo Sys { get; set; } = new();
    [JsonPropertyName("dt")] public long Dt { get; set; }            // 观测时间 unix
    [JsonPropertyName("name")] public string Name { get; set; } = ""; // 城市名
}

/// <summary>预报响应（/forecast），list 为 3 小时步长</summary>
public class OwForecastResponse
{
    [JsonPropertyName("list")] public List<OwForecastItem> List { get; set; } = new();
    [JsonPropertyName("city")] public OwCityInfo City { get; set; } = new();
}

public class OwForecastItem
{
    [JsonPropertyName("dt")] public long Dt { get; set; }
    [JsonPropertyName("main")] public OwMainInfo Main { get; set; } = new();
    [JsonPropertyName("weather")] public List<OwWeather> Weather { get; set; } = new();
    [JsonPropertyName("dt_txt")] public string DtTxt { get; set; } = ""; // "2026-08-24 12:00:00"
}

public class OwWeather
{
    [JsonPropertyName("main")] public string Main { get; set; } = "";    // "Clear"/"Rain"...
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";    // "01d"
}

public class OwMainInfo
{
    [JsonPropertyName("temp")] public double Temp { get; set; }      // units=metric → °C
    [JsonPropertyName("feels_like")] public double FeelsLike { get; set; }
    [JsonPropertyName("temp_min")] public double TempMin { get; set; }
    [JsonPropertyName("temp_max")] public double TempMax { get; set; }
    [JsonPropertyName("humidity")] public int Humidity { get; set; }
}

public class OwWindInfo
{
    [JsonPropertyName("speed")] public double Speed { get; set; }    // m/s
    [JsonPropertyName("deg")] public int Deg { get; set; }           // 风向角度
}

public class OwCloudsInfo
{
    [JsonPropertyName("all")] public int All { get; set; }
}

public class OwSysInfo
{
    [JsonPropertyName("country")] public string Country { get; set; } = "";
    [JsonPropertyName("timezone")] public long Timezone { get; set; } = 0; // 秒
}

public class OwCityInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("country")] public string Country { get; set; } = "";
    [JsonPropertyName("timezone")] public long Timezone { get; set; } = 0;
}