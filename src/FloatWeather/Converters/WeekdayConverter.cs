using System.Globalization;
using System.Windows.Data;

namespace FloatWeather.Converters;

/// <summary>将 DateTime 转为中文星期（周一~周日）</summary>
public sealed class WeekdayConverter : IValueConverter
{
    private static readonly string[] Names =
        ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DateTime dt ? Names[(int)dt.DayOfWeek] : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
