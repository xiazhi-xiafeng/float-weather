using System.IO;
using System.Text.Json;

namespace FloatWeather.Services;

/// <summary>悬浮窗显示外观</summary>
public enum FloaterDisplayMode
{
    /// <summary>玻璃卡片（默认）</summary>
    Glass,
    /// <summary>桌面直显·无卡片，仅文字直接浮在桌面上</summary>
    Bare
}

/// <summary>
/// 本地 UI 状态存储（窗口位置、外观等）。
/// 存放在应用目录 ui-state.json，随构建产物即可用、权限一致。
/// </summary>
public sealed class UiStateService
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "ui-state.json");

    public double FloaterLeft { get; set; } = double.NaN;   // NaN 表示未记忆，走默认位置
    public double FloaterTop { get; set; } = double.NaN;

    /// <summary>鼠标穿透是否开启（重启后保持）</summary>
    public bool ClickThrough { get; set; }
    /// <summary>悬浮窗当前是否可见（重启后保持）</summary>
    public bool FloaterVisible { get; set; } = true;

    /// <summary>显示外观（重启后保持）</summary>
    public FloaterDisplayMode DisplayMode { get; set; } = FloaterDisplayMode.Glass;
    /// <summary>全局透明度 0.2–1.0（重启后保持）</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>拖拽结束贴边吸附的触发距离（像素），0 表示关闭（重启后保持）</summary>
    public double SnapThreshold { get; set; } = 12;

    /// <summary>显示外观被改动（供托盘、设置页双向同步）</summary>
    public event Action? AppearanceChanged;
    public void RaiseAppearanceChanged() => AppearanceChanged?.Invoke();

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = JsonSerializer.Deserialize<UiStateService>(File.ReadAllText(FilePath));
            if (json is not null)
            {
                FloaterLeft = json.FloaterLeft;
                FloaterTop = json.FloaterTop;
                ClickThrough = json.ClickThrough;
                FloaterVisible = json.FloaterVisible;
                DisplayMode = json.DisplayMode;
                if (json.Opacity >= 0.2 && json.Opacity <= 1.0)
                    Opacity = json.Opacity;
                if (json.SnapThreshold >= 0 && json.SnapThreshold <= 60)
                    SnapThreshold = json.SnapThreshold;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[UiState] 加载失败: " + ex.Message);
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, Options);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[UiState] 保存失败: " + ex.Message);
        }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}