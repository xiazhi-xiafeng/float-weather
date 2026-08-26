namespace FloatWeather.Models.Dto;

/// <summary>
/// 温度趋势曲线上的单个数据点。<br/>
/// <see cref="High"/> 为必填上值；单值序列（如逐时观察）令 <see cref="Low"/> 为 null，
/// 双值序列（如逐日高低温）同时提供 Low 与 High 以绘制温差带状。
/// </summary>
public sealed record TrendPoint(string Label, double High, double? Low);