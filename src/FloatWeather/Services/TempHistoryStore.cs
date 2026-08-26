namespace FloatWeather.Services;

/// <summary>一次温度观察快照</summary>
public sealed record TempSnapshot(DateTime Time, double Temp);

/// <summary>
/// 应用运行期内的小时温度历史缓存（滚动保留最近一天）。
/// 由悬浮窗定时刷新时写入，供详情窗绘制"过去 24h"温度趋势曲线。
/// </summary>
public sealed class TempHistoryStore
{
    private readonly object _lock = new();
    private readonly List<TempSnapshot> _points = new();
    private DateTime _lastHour = DateTime.MinValue;

    /// <summary>记录一次温度观察；同一自然小时只保留最新一条。</summary>
    public void Record(double temp, DateTime time)
    {
        lock (_lock)
        {
            var kind = time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Local : time.Kind;
            var hour = new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0, kind);
            if (hour == _lastHour && _points.Count > 0)
            {
                // 同小时更新为最新读数
                var idx = _points.Count - 1;
                _points[idx] = new TempSnapshot(time, temp);
                return;
            }
            _lastHour = hour;

            var cutoff = time.AddHours(-24);
            _points.RemoveAll(p => p.Time < cutoff);
            _points.Add(new TempSnapshot(time, temp));
        }
    }

    /// <summary>返回以 <paramref name="now"/> 为基准、最近 <paramref name="hours"/> 小时内按时间升序的观察点。</summary>
    public IReadOnlyList<TempSnapshot> GetPast(int hours, DateTime now)
    {
        lock (_lock)
        {
            var cutoff = now.AddHours(-hours);
            return _points.Where(p => p.Time >= cutoff).OrderBy(p => p.Time).ToList();
        }
    }
}