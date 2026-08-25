using System.Text.Json.Serialization;

namespace FloatWeather.Models.Dto.Sources;

/// <summary>心知天气 /v3/weather/ 系列响应（now / hourly / daily 共用外层）</summary>
public sealed class SeniverseResponse
{
    public List<SeniResult> Results { get; set; } = new();
}

public sealed class SeniResult
{
    public SeniLocation? Location { get; set; }
    public SeniNow? Now { get; set; }
    public List<SeniDaily> Daily { get; set; } = new();
    public List<SeniHourly> Hourly { get; set; } = new();
    public string LastUpdate { get; set; } = "";
}

public sealed class SeniLocation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>天气实况 now.json</summary>
public sealed class SeniNow
{
    public string Temperature { get; set; } = "";
    public string FeelsLike { get; set; } = "";
    public string Text { get; set; } = "";
    public string Code { get; set; } = "";

    public string WindDirection { get; set; } = "";
    public string WindScale { get; set; } = "";
    public string WindSpeed { get; set; } = "";
    public string Humidity { get; set; } = "";
}

/// <summary>逐日 daily.json</summary>
public sealed class SeniDaily
{
    public string Date { get; set; } = "";
    public string TextDay { get; set; } = "";
    public string CodeDay { get; set; } = "";
    public string TextNight { get; set; } = "";
    public string CodeNight { get; set; } = "";

    public string High { get; set; } = "";
    public string Low { get; set; } = "";
    public string WindDirection { get; set; } = "";
    public string WindScale { get; set; } = "";
}

/// <summary>逐时 hourly.json</summary>
public sealed class SeniHourly
{
    public string Time { get; set; } = "";
    public string Text { get; set; } = "";
    public string Code { get; set; } = "";
    public string Temperature { get; set; } = "";
}