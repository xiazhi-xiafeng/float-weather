using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FloatWeather.Services;
using FloatWeather.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FloatWeather.Views;

/// <summary>可拖拽悬浮小组件窗：悬停逐时浮层 + 鼠标穿透 + 系统托盘</summary>
public partial class FloaterWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private readonly FloaterViewModel _vm;
    private readonly ConfigService _config;
    private readonly UiStateService _ui;
    private readonly AutoStartService _autoStart;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly System.Windows.Threading.DispatcherTimer _brightTimer;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _trayBare;
    private ToolStripMenuItem? _trayFloater;
    private ToolStripMenuItem? _trayClickThrough;
    private bool _clickThrough;

    public FloaterWindow(FloaterViewModel vm, ConfigService config,
        UiStateService ui, AutoStartService autoStart)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _config = config;
        _ui = ui;
        _autoStart = autoStart;

        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(config.Weather.RefreshIntervalSeconds, 60))
        };
        _timer.Tick += async (_, _) => await _vm.RefreshAsync();

        // 桌面直显下的背景亮度感知：定期采样悬浮窗区域桌面亮度，自动切换深/白文字
        _brightTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _brightTimer.Tick += (_, _) => SampleBrightness();

        _ui.Load();
        _clickThrough = _ui.ClickThrough;   // 恢复上次的穿透状态
        RestorePosition();

        Loaded += async (_, _) =>
        {
            // 先应用持久化的穿透状态（需 hwnd），再应用外观，其次建托盘，最后按可见性决定是否显示
            SetClickThrough(_clickThrough, notify: false);
            SetAppearance(_ui.DisplayMode == FloaterDisplayMode.Bare, _ui.Opacity);
            InitializeTray();
            if (!_ui.FloaterVisible) Hide();
            await _vm.RefreshAsync();
            _timer.Start();
            _brightTimer.Start();
        };
        Closed += (_, _) =>
        {
            _ui.FloaterLeft = Left;
            _ui.FloaterTop = Top;
            _ui.Save();
            _brightTimer.Stop();
            _tray?.Dispose();
        };
    }

    /// <summary>恢复上次窗口位置；超出当前屏幕则忽略走默认位置</summary>
    private void RestorePosition()
    {
        if (!double.IsFinite(_ui.FloaterLeft) || !double.IsFinite(_ui.FloaterTop)) return;
        var left = System.Windows.SystemParameters.VirtualScreenLeft;
        var top = System.Windows.SystemParameters.VirtualScreenTop;
        var right = left + System.Windows.SystemParameters.VirtualScreenWidth;
        var bottom = top + System.Windows.SystemParameters.VirtualScreenHeight;
        if (_ui.FloaterLeft >= left &&
            _ui.FloaterLeft + Width <= right &&
            _ui.FloaterTop >= top &&
            _ui.FloaterTop + Height <= bottom)
        {
            Left = _ui.FloaterLeft;
            Top = _ui.FloaterTop;
        }
        else
        {
            _ui.FloaterLeft = double.NaN;
            _ui.FloaterTop = double.NaN;
        }
    }

    /// <summary>拖拽移动窗口</summary>
    private void DragMove_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* 忽略拖拽异常 */ }
        }
    }

    /// <summary>悬停显示逐时天气</summary>
    private void Floater_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_clickThrough && _vm.HasHourly)
            HourlyPopup.IsOpen = true;
    }

    private void Floater_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HourlyPopup.IsOpen = false;
    }

    /// <summary>点击 ▶ 展开详情窗口</summary>
    private void OpenDetail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => OpenDetail();

    private void OpenDetail()
    {
        var detail = new MainWindow(App.Services!.GetRequiredService<DetailViewModel>());
        detail.Show();
    }

    /// <summary>打开设置窗口</summary>
    private void OpenSettings()
    {
        var settings = App.Services!.GetRequiredService<SettingsWindow>();
        settings.Show();
    }

    /// <summary>配置变更后由设置窗调用：刷新数据源状态、更新刷新间隔并触发一次刷新（即时生效）</summary>
    public void ApplyConfig()
    {
        App.Services!.GetRequiredService<SourceManager>().RefreshEnabled();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(_config.Weather.RefreshIntervalSeconds, 60));
        _ = _vm.RefreshAsync();
    }

    private void InitializeTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "悬浮天气",
            Visible = true
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();

        // 显示/隐藏悬浮窗：找回入口（穿透开启时鼠标点不到，只能靠托盘/设置恢复）
        _trayFloater = new System.Windows.Forms.ToolStripMenuItem("隐藏悬浮窗") { Checked = _ui.FloaterVisible };
        _trayFloater.Click += (_, _) => SetFloaterVisible(!_ui.FloaterVisible);

        var detail = new System.Windows.Forms.ToolStripMenuItem("打开详情");
        detail.Click += (_, _) => OpenDetail();

        var settings = new System.Windows.Forms.ToolStripMenuItem("设置");
        settings.Click += (_, _) => OpenSettings();

        // 鼠标穿透：勾选即当前状态（带反馈）
        _trayClickThrough = new System.Windows.Forms.ToolStripMenuItem("鼠标穿透") { Checked = _clickThrough };
        _trayClickThrough.Click += (_, _) => ToggleClickThrough();

        _trayBare = new System.Windows.Forms.ToolStripMenuItem("桌面直显") { Checked = _ui.DisplayMode == FloaterDisplayMode.Bare };
        _trayBare.Click += (_, _) => SetAppearance(!(_ui.DisplayMode == FloaterDisplayMode.Bare), _ui.Opacity);

        var autostart = new System.Windows.Forms.ToolStripMenuItem("开机自启") { Checked = _autoStart.IsEnabled };
        autostart.Click += (_, _) =>
        {
            _autoStart.Set(!autostart.Checked);
            autostart.Checked = !autostart.Checked;
        };

        var refresh = new System.Windows.Forms.ToolStripMenuItem("刷新天气");
        refresh.Click += async (_, _) => await _vm.RefreshAsync();

        var exit = new System.Windows.Forms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        menu.Items.Add(_trayFloater);
        menu.Items.Add(detail);
        menu.Items.Add(settings);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_trayClickThrough);
        menu.Items.Add(_trayBare);
        menu.Items.Add(autostart);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(refresh);
        menu.Items.Add(exit);

        _tray.ContextMenuStrip = menu;
    }

    /// <summary>设置悬浮窗可见性：托盘与设置页共用同一恢复/隐藏入口</summary>
    public void SetFloaterVisible(bool visible)
    {
        _ui.FloaterVisible = visible;
        _ui.Save();
        if (visible)
        {
            Show();
            Activate();
            Topmost = true;
        }
        else
        {
            Hide();
        }
        UpdateTrayChecks();
    }

    public bool IsClickThrough => _clickThrough;

    /// <summary>应用显示外观（桌面直显 + 透明度），持久化并写入 VM 驱动视图即时生效。</summary>
    public void SetAppearance(bool bare, double opacity)
    {
        _ui.DisplayMode = bare ? FloaterDisplayMode.Bare : FloaterDisplayMode.Glass;
        _ui.Opacity = Math.Clamp(opacity, 0.2, 1.0);
        _ui.Save();
        _vm.IsBare = bare;
        _vm.Opacity = _ui.Opacity;   // Window.Opacity 已绑定该值
        _ui.RaiseAppearanceChanged(); // 通知设置页实时同步
        UpdateTrayChecks();
    }

    private void ToggleClickThrough()
    {
        SetClickThrough(!_clickThrough, notify: true);
    }

    /// <summary>应用鼠标穿透状态（WS_EX_TRANSPARENT），持久化并同步托盘勾选。</summary>
    public void SetClickThrough(bool enabled, bool notify = false)
    {
        _clickThrough = enabled;
        _ui.ClickThrough = enabled;
        _ui.Save();

        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = enabled ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        HourlyPopup.IsOpen = false;
        if (notify)
            _tray?.ShowBalloonTip(2000, "悬浮天气",
                enabled ? "已开启鼠标穿透 · 解除请点这里" : "已关闭鼠标穿透",
                System.Windows.Forms.ToolTipIcon.Info);
        UpdateTrayChecks();
    }

    /// <summary>同步托盘菜单"显示/隐藏悬浮窗"与"鼠标穿透"两条的勾选与文案</summary>
    private void UpdateTrayChecks()
    {
        if (_trayFloater is not null)
        {
            _trayFloater.Text = _ui.FloaterVisible ? "隐藏悬浮窗" : "显示悬浮窗";
            _trayFloater.Checked = _ui.FloaterVisible;
        }
        if (_trayClickThrough is not null)
            _trayClickThrough.Checked = _clickThrough;
        if (_trayBare is not null)
            _trayBare.Checked = _ui.DisplayMode == FloaterDisplayMode.Bare;
    }

    /// <summary>采样悬浮窗区域桌面亮度，驱动桌面直显下自动切换深/白文字。</summary>
    private void SampleBrightness()
    {
        // 玻璃卡片有主题底色，不依赖背景；仅桌面直显时按背景明暗着色
        if (!_vm.IsBare || Visibility != Visibility.Visible) return;
        try
        {
            var scale = VisualTreeHelper.GetDpi(this);
            int x = Math.Max(0, (int)Math.Round((Left - SystemParameters.VirtualScreenLeft) * scale.DpiScaleX));
            int y = Math.Max(0, (int)Math.Round((Top - SystemParameters.VirtualScreenTop) * scale.DpiScaleY));
            int w = Math.Max(1, (int)Math.Round(ActualWidth * scale.DpiScaleX));
            int h = Math.Max(1, (int)Math.Round(ActualHeight * scale.DpiScaleY));

            using var full = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(full))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            using var thumb = new System.Drawing.Bitmap(6, 4);
            using (var g2 = System.Drawing.Graphics.FromImage(thumb))
                g2.DrawImage(full, 0, 0, 6, 4);

            double sum = 0;
            int n = 0;
            for (int yy = 0; yy < thumb.Height; yy++)
                for (int xx = 0; xx < thumb.Width; xx++)
                {
                    var p = thumb.GetPixel(xx, yy);
                    sum += 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
                    n++;
                }
            double avg = n > 0 ? sum / n : 255;
            // 迟滞判定：进入/退出暗背景用不同阈值，中间留保持带，避免背景亮度在临界点使文字深/白反复横跳
            bool dark = _vm.IsDarkBackground ? avg < 165 : avg < 135;
            if (dark != _vm.IsDarkBackground)
                _vm.IsDarkBackground = dark;
        }
        catch
        {
            // 截图失败（锁屏/最小化等）保持上次状态，下轮重试
        }
    }

    /// <summary>加载内嵌的天气托盘图标；缺失时回退系统默认图标</summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("tray.ico", StringComparison.OrdinalIgnoreCase));
        if (name is null) return System.Drawing.SystemIcons.Application;
        using var s = asm.GetManifestResourceStream(name);
        return s is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(s);
    }

    // 64 位窗口样式操作（WPF 桌面为 AnyCPU/64 位运行）
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);
}