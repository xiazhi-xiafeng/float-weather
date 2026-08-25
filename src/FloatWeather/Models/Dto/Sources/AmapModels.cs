namespace FloatWeather.Models.Dto.Sources;

// 高德 Web 服务天气接口（restapi.amap.com/v3/weather）
// 实时 lives[]；预报 forecasts[0].casts[]。字段为 camelCase。

public class AmapResponse
{
    public string Status { get; set; } = "";   // 1=成功
    public string Info { get; set; } = "";
    public string Infocode { get; set; } = "";
    public List<AmapLive> Lives { get; set; } = new();
    public List<AmapForecast> Forecasts { get; set; } = new();
}

public class AmapLive
{
    public string Province { get; set; } = "";
    public string City { get; set; } = "";
    public string Adcode { get; set; } = "";
    public string Weather { get; set; } = "";         // 晴/多云/...
    public string Temperature { get; set; } = "";
    public string Winddirection { get; set; } = "";
    public string Windpower { get; set; } = "";       // 1级/2级...
    public string Humidity { get; set; } = "";
    public string ReportTime { get; set; } = "";
}

public class AmapForecast
{
    public string City { get; set; } = "";
    public string Adcode { get; set; } = "";
    public string ReportTime { get; set; } = "";
    public List<AmapCast> Casts { get; set; } = new();
}

public class AmapCast
{
    public string Date { get; set; } = "";
    public string DayWeather { get; set; } = "";
    public string NightWeather { get; set; } = "";
    public string DayTemp { get; set; } = "";
    public string NightTemp { get; set; } = "";
    public string DayWind { get; set; } = "";
    public string NightWind { get; set; } = "";
    public string DayPower { get; set; } = "";
    public string NightPower { get; set; } = "";
}