namespace FloatWeather.Models.Dto.Sources;

// 和风天气 API v7 响应模型（属性用 PascalCase，反序列化时走 CamelCase + 忽略大小写）

public class QwNowResponse { public string Code { get; set; } = ""; public QwNow? Now { get; set; } }
public class QwNow
{
    public string ObsTime { get; set; } = "";
    public string Temp { get; set; } = "";
    public string FeelsLike { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Text { get; set; } = "";
    public string Wind360 { get; set; } = "";
    public string WindDir { get; set; } = "";
    public string WindScale { get; set; } = "";
    public string WindSpeed { get; set; } = "";
    public string Humidity { get; set; } = "";
}

public class QwHourlyResponse { public string Code { get; set; } = ""; public List<QwHour>? Hourly { get; set; } }
public class QwHour
{
    public string FxTime { get; set; } = "";
    public string Temp { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Text { get; set; } = "";
}

public class QwDailyResponse { public string Code { get; set; } = ""; public List<QwDay>? Daily { get; set; } }
public class QwDay
{
    public string FxDate { get; set; } = "";
    public string TempMax { get; set; } = "";
    public string TempMin { get; set; } = "";
    public string IconDay { get; set; } = "";
    public string TextDay { get; set; } = "";
    public string WindDirDay { get; set; } = "";
    public string WindScaleDay { get; set; } = "";
}

public class QwAirResponse { public string Code { get; set; } = ""; public QwAir? Now { get; set; } }
public class QwAir
{
    public string PubTime { get; set; } = "";
    public string Aqi { get; set; } = "";
    public string Category { get; set; } = "";
    public string Primary { get; set; } = "";
}

public class QwIndexResponse { public string Code { get; set; } = ""; public List<QwIndex>? Daily { get; set; } }
public class QwIndex
{
    public string Date { get; set; } = "";
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Text { get; set; } = "";
}