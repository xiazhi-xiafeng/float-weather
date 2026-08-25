namespace FloatWeather.ViewModels;

/// <summary>数据源配置弹窗中的一行字段</summary>
public sealed class ProviderFieldRow
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Multiline { get; set; }
}
