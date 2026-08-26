namespace FloatWeather.Services;

/// <summary>相对时间文案工具</summary>
public static class TimeText
{
    /// <summary>相对时间：刚刚 / N分钟前 / N小时前 / N天前</summary>
    public static string Ago(DateTime t)
    {
        var span = DateTime.Now - t;
        if (span < TimeSpan.FromSeconds(60)) return "刚刚";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}分钟前";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}小时前";
        return $"{(int)span.TotalDays}天前";
    }
}