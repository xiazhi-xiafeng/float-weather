using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FloatWeather.Services;
using FloatWeather.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FloatWeather.ViewModels;

/// <summary>设置页：单一数据源健康状态行</summary>
public sealed class SourceHealthItem
{
    public string Name { get; init; } = "";
    public bool IsEnabled { get; init; }
    public int FailCount { get; init; }
    public bool IsOpen { get; init; }
    public string LastError { get; init; } = "";

    /// <summary>状态文本：正常 / 熔断中 / 未配置</summary>
    public string StateText =>
        !IsEnabled ? "未配置"
        : IsOpen ? "熔断中"
        : FailCount > 0 ? $"失败{FailCount}次"
        : "正常";
}

/// <summary>设置窗口 VM</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly SourceManager _sourceManager;
    private readonly UiStateService _ui;
    private bool _loaded;
    private bool _suppressAppearance;   // 外部（托盘）同步时抑制回写，避免双向循环

    [ObservableProperty] private string cityName = "";
    [ObservableProperty] private int refreshIntervalMinutes = 30;
    [ObservableProperty] private string saveMessage = "";

    /// <summary>刷新间隔档位（分钟）</summary>
    public System.Collections.ObjectModel.ObservableCollection<int> RefreshIntervalOptions { get; } = new() { 5, 10, 15, 30, 60, 120 };

    // 悬浮窗可见性与鼠标穿透：两处（托盘/设置）共享同一状态，切换即时生效
    [ObservableProperty] private bool clickThrough;
    [ObservableProperty] private bool floaterVisible = true;

    // 主/备数据源选择
    [ObservableProperty] private string primaryProvider = "";
    [ObservableProperty] private string fallbackProvider = "";
    public System.Collections.ObjectModel.ObservableCollection<string> ProviderNames { get; } = new();

    // 悬浮窗显示外观与透明度（与悬浮窗即时同步、持久化）
    [ObservableProperty] private string displayMode = "玻璃卡片";
    [ObservableProperty] private int opacityPercent = 100;
    public System.Collections.ObjectModel.ObservableCollection<string> DisplayModeOptions { get; } = new() { "玻璃卡片", "桌面直显" };

    // 和风数据源配置
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QwConfigured))]
    private string qwProjectId = "";
    [ObservableProperty] private string qwCredentialId = "";
    [ObservableProperty] private string qwPrivateKey = "";
    [ObservableProperty] private string qwApiHost = "";

    // 高德数据源配置
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmapConfigured))]
    private string amapKey = "";

    // OpenWeather 数据源配置
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OwConfigured))]
    private string openWeatherKey = "";

    // 心知天气数据源配置
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SnConfigured))]
    private string seniverseKey = "";

    /// <summary>和风是否已配置（以项目 ID 为准）</summary>
    public bool QwConfigured => !string.IsNullOrWhiteSpace(QwProjectId);
    /// <summary>高德是否已配置</summary>
    public bool AmapConfigured => !string.IsNullOrWhiteSpace(AmapKey);
    /// <summary>OpenWeather 是否已配置</summary>
    public bool OwConfigured => !string.IsNullOrWhiteSpace(OpenWeatherKey);
    /// <summary>心知天气是否已配置</summary>
    public bool SnConfigured => !string.IsNullOrWhiteSpace(SeniverseKey);

    public System.Collections.ObjectModel.ObservableCollection<SourceHealthItem> Sources { get; } = new();

    public SettingsViewModel(ConfigService config, SourceManager sourceManager, UiStateService ui)
    {
        _config = config;
        _sourceManager = sourceManager;
        _ui = ui;
        ClickThrough = _ui.ClickThrough;
        FloaterVisible = _ui.FloaterVisible;
        _ui.AppearanceChanged += OnExternalAppearanceChanged;
        ReloadFromConfig();
        RefreshHealth();
        _loaded = true;    // 初始化完成后，属性变更即可即时应用到悬浮窗
    }

    /// <summary>托盘改动外观（桌面直显/透明度）时同步到设置页，抑制回写避免循环</summary>
    private void OnExternalAppearanceChanged()
    {
        _suppressAppearance = true;
        DisplayMode = _ui.DisplayMode == FloaterDisplayMode.Bare ? "桌面直显" : "玻璃卡片";
        OpacityPercent = (int)Math.Round(_ui.Opacity * 100);
        _suppressAppearance = false;
    }

    partial void OnClickThroughChanged(bool value)
    {
        if (_loaded) ApplyFloater(f => f.SetClickThrough(value));
    }

    partial void OnFloaterVisibleChanged(bool value)
    {
        if (_loaded) ApplyFloater(f => f.SetFloaterVisible(value));
    }

    partial void OnDisplayModeChanged(string value)
    {
        if (_loaded && !_suppressAppearance) ApplyFloater(f => f.SetAppearance(value == "桌面直显", OpacityPercent / 100.0));
    }

    partial void OnOpacityPercentChanged(int value)
    {
        if (_loaded && !_suppressAppearance) ApplyFloater(f => f.SetAppearance(DisplayMode == "桌面直显", value / 100.0));
    }

    private static void ApplyFloater(Action<FloaterWindow> apply)
    {
        var floater = App.Services?.GetService<FloaterWindow>();
        if (floater is not null) apply(floater);
    }

    private void ReloadFromConfig()
    {
        var w = _config.Weather;
        CityName = w.CityName;
        RefreshIntervalMinutes = SnapToOption(w.RefreshIntervalSeconds / 60);
        PrimaryProvider = string.IsNullOrWhiteSpace(w.PrimaryProvider) ? "默认顺序" : w.PrimaryProvider;
        FallbackProvider = string.IsNullOrWhiteSpace(w.FallbackProvider) ? "默认顺序" : w.FallbackProvider;
        DisplayMode = _ui.DisplayMode == FloaterDisplayMode.Bare ? "桌面直显" : "玻璃卡片";
        OpacityPercent = (int)Math.Round(_ui.Opacity * 100);

        // 填充主/备可选数据源（已配置 Key 的源）
        ProviderNames.Clear();
        ProviderNames.Add("默认顺序"); // 留空 = 默认注册顺序
        foreach (var h in _sourceManager.Health.Values)
            if (h.IsEnabled) ProviderNames.Add(h.Name);

        var qw = _config.QWeather;
        QwProjectId = qw.ProjectId;
        QwCredentialId = qw.CredentialId;
        QwPrivateKey = qw.PrivateKey;
        QwApiHost = qw.ApiHost;

        AmapKey = _config.Amap.Key;
        OpenWeatherKey = _config.OpenWeather.Key;
        SeniverseKey = _config.Seniverse.Key;
    }

    private void RefreshHealth()
    {
        Sources.Clear();
        foreach (var h in _sourceManager.Health.Values)
        {
            Sources.Add(new SourceHealthItem
            {
                Name = h.Name,
                IsEnabled = h.IsEnabled,
                FailCount = h.FailCount,
                IsOpen = h.IsOpen,
                LastError = h.LastError
            });
        }
    }

    [RelayCommand]
    private void RefreshHealthCmd() => RefreshHealth();

    /// <summary>由设置窗「保存设置」按钮调用</summary>
    public void Save()
    {
        var interval = RefreshIntervalMinutes * 60;
        _config.SaveWeather(new WeatherAppConfig
        {
            CityName = CityName.Trim(),
            RefreshIntervalSeconds = interval,
            PrimaryProvider = PrimaryProvider == "默认顺序" ? "" : PrimaryProvider,
            FallbackProvider = FallbackProvider == "默认顺序" ? "" : FallbackProvider
        });
        _config.SaveProviders(
            new QWeatherConfig
            {
                ProjectId = QwProjectId.Trim(),
                CredentialId = QwCredentialId.Trim(),
                PrivateKey = QwPrivateKey.Trim(),
                ApiHost = QwApiHost.Trim()
            },
            new AmapConfig
            {
                Key = AmapKey.Trim()
            },
            new OpenWeatherConfig
            {
                Key = OpenWeatherKey.Trim()
            },
            new SeniverseConfig
            {
                Key = SeniverseKey.Trim()
            });
        SaveMessage = $"已保存（城市 {CityName.Trim()}，刷新间隔 {RefreshIntervalMinutes} 分钟）";
    }

    /// <summary>把秒数换算的分钟吸附到最近的档位，保证 ComboBox 能选中</summary>
    private int SnapToOption(int minutes)
    {
        if (RefreshIntervalOptions.Contains(minutes)) return minutes;
        int best = RefreshIntervalOptions[0];
        foreach (var o in RefreshIntervalOptions)
            if (Math.Abs(o - minutes) < Math.Abs(best - minutes)) best = o;
        return best;
    }
}