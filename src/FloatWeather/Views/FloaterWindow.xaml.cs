using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FloatWeather.Services;
using Microsoft.Win32;
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
    private readonly System.Windows.Threading.DispatcherTimer _brightDebounce;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _trayBare;
    private ToolStripMenuItem? _trayFloater;
    private ToolStripMenuItem? _trayClickThrough;
    private bool _clickThrough;

    // 悬浮窗右键菜单（WPF）+ 详情窗防重复
    private System.Windows.Controls.ContextMenu? _floaterContextMenu;
    private System.Windows.Controls.MenuItem? _menuVisible;
    private System.Windows.Controls.MenuItem? _menuClickThrough;
    private System.Windows.Controls.MenuItem? _menuBare;
    private System.Windows.Controls.MenuItem? _menuAutostart;
    private Window? _detailWindow;

    // 交互增强：逐时浮层延迟关闭 + 低透明度 hover 显形 + 穿透悬停提示
    private readonly System.Windows.Threading.DispatcherTimer _hourlyCloseDebounce;
    private readonly System.Windows.Threading.DispatcherTimer _ctHoverTimer;
    private bool _ctNotified;

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

        // 桌面直显下的背景亮度感知：改为"拖拽结束/显示变化"事件驱动采样，
        // 用 300ms 防抖只保留"停下那一刻"的一次采样，替代原先的 2s 常驻轮询
        _brightDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _brightDebounce.Tick += (_, _) =>
        {
            _brightDebounce.Stop();
            SampleBrightness();
        };

        // 逐时浮层延迟关闭：鼠标从卡片移到浮层途中不闪关，停留 250ms 后再关
        _hourlyCloseDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _hourlyCloseDebounce.Tick += (_, _) =>
        {
            _hourlyCloseDebounce.Stop();
            HourlyPopup.IsOpen = false;
        };

        // 穿透悬停提示：只在穿透开启且本窗可见时启动低频率轮询
        _ctHoverTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _ctHoverTimer.Tick += (_, _) => OnCtHoverTick();

        _ui.Load();
        _clickThrough = _ui.ClickThrough;   // 恢复上次的穿透状态
        RestorePosition();

        Loaded += async (_, _) =>
        {
            // 先应用持久化的穿透状态（需 hwnd），再应用外观，其次建托盘，最后按可见性决定是否显示
            SetClickThrough(_clickThrough, notify: false);
            SetAppearance(_ui.DisplayMode == FloaterDisplayMode.Bare, _ui.Opacity);
            InitializeTray();
            InitializeContextMenu();
            TempText.AddHandler(System.Windows.Data.Binding.TargetUpdatedEvent,
                new EventHandler<System.Windows.Data.DataTransferEventArgs>(OnTempTargetUpdated));
            if (!_ui.FloaterVisible) Hide();
            await _vm.RefreshAsync();
            _timer.Start();

            // 事件驱动采样：拖动/显示变化触发；显示器热插拔/分辨率变更兜底拉回
            LocationChanged += OnFloaterLocationChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            if (Visibility == Visibility.Visible) SampleBrightness();
        };
        Closed += (_, _) =>
        {
            _ui.FloaterLeft = Left;
            _ui.FloaterTop = Top;
            _ui.Save();
            _brightDebounce.Stop();
            _hourlyCloseDebounce.Stop();
            _ctHoverTimer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
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

    /// <summary>显示器热插拔/分辨率变更：窗口可能被挤出可视区，拉回最近屏幕默认角并持久化。</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SystemEvents 在后台线程回调，切回 UI 线程处理
        Dispatcher.BeginInvoke(EnsureOnScreen);
    }

    /// <summary>确保悬浮窗位于当前虚拟屏内；掉出则复位到主屏右下角。</summary>
    private void EnsureOnScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        double cx = Left + Width / 2;
        double cy = Top + Height / 2;
        if (cx >= left && cx <= right && cy >= top && cy <= bottom) return; // 仍在可视区

        Left = right - Width - 16;
        Top = bottom - Height + 4;
        _ui.FloaterLeft = Left;
        _ui.FloaterTop = Top;
        _ui.Save();
        SampleBrightness();
    }

    /// <summary>拖拽/移动会高频触发 LocationChanged，用防抖只采一次"停下后"的桌面亮度。</summary>
    private void OnFloaterLocationChanged(object? sender, EventArgs e)
    {
        _brightDebounce.Stop();
        _brightDebounce.Start();
    }

    /// <summary>贴边吸附：拖拽结束时若贴近屏幕某一边缘(阈值内)，自动对齐到该边缘。</summary>
    private void SnapWindowToEdge()
    {
        double threshold = _ui.SnapThreshold;
        if (threshold <= 0) return;   // 已关闭贴边吸附
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;

        bool changed = false;
        // 横向贴边
        if (Math.Abs(Left - left) <= threshold && Left != left)
        {
            Left = left;
            changed = true;
        }
        else if (Math.Abs(Left + Width - right) <= threshold && Left + Width != right)
        {
            Left = right - Width;
            changed = true;
        }
        // 纵向贴边
        if (Math.Abs(Top - top) <= threshold && Top != top)
        {
            Top = top;
            changed = true;
        }
        else if (Math.Abs(Top + Height - bottom) <= threshold && Top + Height != bottom)
        {
            Top = bottom - Height;
            changed = true;
        }

        if (changed)
        {
            _ui.FloaterLeft = Left;
            _ui.FloaterTop = Top;
            _ui.Save();
        }
    }

    /// <summary>拖拽移动窗口</summary>
    private void DragMove_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 双击整卡打开详情，单击左键才进入拖拽（DragMove 拖拽与双击互不冲突）
        if (e.ClickCount >= 2 && e.ButtonState == MouseButtonState.Pressed)
        {
            OpenDetail();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
                SnapWindowToEdge();   // 拖拽结束后贴边
            }
            catch { /* 忽略拖拽异常 */ }
        }
    }

    /// <summary>悬停显示逐时天气</summary>
    private void Floater_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hourlyCloseDebounce.Stop();
        if (_clickThrough) return;
        // 低透明度 hover 显形：透明度低时鼠标移入临时提亮，移出恢复基准值
        if (Opacity < Math.Max(_vm.Opacity, 0.9))
            Opacity = Math.Max(_vm.Opacity, 0.9);
        if (_vm.HasHourly)
            HourlyPopup.IsOpen = true;
    }

    private void Floater_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Opacity = _vm.Opacity;   // 恢复基准透明度
        _hourlyCloseDebounce.Stop();
        _hourlyCloseDebounce.Start();
    }

    /// <summary>点击 ▶ 展开详情窗口</summary>
    private void OpenDetail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => OpenDetail();

    private void OpenDetail()
    {
        // 防重复打开：详情窗已在显示则直接激活
        if (_detailWindow is { IsLoaded: true })
        {
            _detailWindow.Activate();
            return;
        }
        var detail = new MainWindow(App.Services!.GetRequiredService<DetailViewModel>());
        _detailWindow = detail;
        detail.Closed += (_, _) => _detailWindow = null;
        detail.Show();
    }

    /// <summary>悬浮窗右键菜单：与托盘菜单同构，方便穿透关闭后仍可快速操作</summary>
    private void InitializeContextMenu()
    {
        _floaterContextMenu = new System.Windows.Controls.ContextMenu();

        _menuVisible = NewMenuItem("隐藏悬浮窗");
        _menuVisible.Click += (_, _) => SetFloaterVisible(!_ui.FloaterVisible);

        var detail = NewMenuItem("打开详情");
        detail.Click += (_, _) => OpenDetail();

        var settings = NewMenuItem("设置");
        settings.Click += (_, _) => OpenSettings();

        _menuClickThrough = NewMenuItem("鼠标穿透", isCheckable: true);
        _menuClickThrough.Click += (_, _) => SetClickThrough(!_clickThrough, notify: false);

        _menuBare = NewMenuItem("桌面直显", isCheckable: true);
        _menuBare.Click += (_, _) => SetAppearance(!(_ui.DisplayMode == FloaterDisplayMode.Bare), _ui.Opacity);

        _menuAutostart = NewMenuItem("开机自启", isCheckable: true);
        _menuAutostart.Click += (_, _) =>
        {
            _autoStart.Set(!_autoStart.IsEnabled);
            _menuAutostart.IsChecked = _autoStart.IsEnabled;
        };

        var refresh = NewMenuItem("刷新天气");
        refresh.Click += async (_, _) => await _vm.RefreshAsync();

        var exit = NewMenuItem("退出");
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        _floaterContextMenu.Items.Add(_menuVisible);
        _floaterContextMenu.Items.Add(detail);
        _floaterContextMenu.Items.Add(settings);
        _floaterContextMenu.Items.Add(NewSeparator());
        _floaterContextMenu.Items.Add(_menuClickThrough);
        _floaterContextMenu.Items.Add(_menuBare);
        _floaterContextMenu.Items.Add(_menuAutostart);
        _floaterContextMenu.Items.Add(NewSeparator());
        _floaterContextMenu.Items.Add(refresh);
        _floaterContextMenu.Items.Add(exit);

        Card.ContextMenu = _floaterContextMenu;
        // 打开时同步各开关的当前状态
        Card.ContextMenuOpening += (_, _) => SyncContextMenu();
    }

    /// <summary>按当前真实状态同步右键菜单（显示/隐藏文案 + 各开关勾选）</summary>
    private void SyncContextMenu()
    {
        _menuVisible!.Header = _ui.FloaterVisible ? "隐藏悬浮窗" : "显示悬浮窗";
        _menuClickThrough!.IsChecked = _clickThrough;
        _menuBare!.IsChecked = _ui.DisplayMode == FloaterDisplayMode.Bare;
        _menuAutostart!.IsChecked = _autoStart.IsEnabled;
    }

    private static System.Windows.Controls.MenuItem NewMenuItem(string header, bool isCheckable = false) =>
        new System.Windows.Controls.MenuItem { Header = header, IsCheckable = isCheckable };

    private static System.Windows.Controls.Separator NewSeparator() => new();

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
            SampleBrightness();   // 显形时补一次采样，避免桌面直显文字颜色滞后
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
        // 穿透开启时低频率检测"鼠标已移到悬浮窗上"并给一次气泡提示，防止找不到解除入口
        if (enabled) _ctHoverTimer.Start();
        else { _ctHoverTimer.Stop(); _ctNotified = false; }
        if (notify)
            _tray?.ShowBalloonTip(2000, "悬浮天气",
                enabled ? "已开启鼠标穿透 · 解除请点这里" : "已关闭鼠标穿透",
                System.Windows.Forms.ToolTipIcon.Info);
        UpdateTrayChecks();
    }

    /// <summary>穿透开启时：鼠标若落在悬浮窗区域内，气泡提示一次"解除请点托盘"，移出后复位以便再次提示。</summary>
    private void OnCtHoverTick()
    {
        if (!_clickThrough || Visibility != Visibility.Visible) { _ctNotified = false; return; }
        if (IsCursorOverWindow())
        {
            if (!_ctNotified)
            {
                _ctNotified = true;
                _tray?.ShowBalloonTip(2000, "悬浮天气", "鼠标穿透已开启 · 解除请点托盘悬浮天气图标",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
        }
        else _ctNotified = false;
    }

    /// <summary>温度值刷新时做一次轻声淡入转场，避免数字突变生硬</summary>
    private void OnTempTargetUpdated(object? sender, DataTransferEventArgs e)
    {
        var fade = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        TempText.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>本窗实际屏幕矩形（物理像素）内是否包含当前鼠标位置</summary>
    private bool IsCursorOverWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;
        return GetWindowRect(hwnd, out RECT r)
            && GetCursorPos(out POINT pt)
            && pt.X >= r.Left && pt.X <= r.Right
            && pt.Y >= r.Top && pt.Y <= r.Bottom;
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

    /// <summary>采样悬浮窗四周桌面亮度，驱动桌面直显下自动切换深/白文字。</summary>
    /// 改为"四周角采样"：避开悬浮窗自身像素，且只移/显变化时才被防抖调用，不再常驻轮询。
    private void SampleBrightness()
    {
        // 玻璃卡片有主题底色，不依赖背景；仅桌面直显时按背景明暗着色
        if (!_vm.IsBare || Visibility != Visibility.Visible) return;
        try
        {
            var scale = VisualTreeHelper.GetDpi(this);
            var vsl = SystemParameters.VirtualScreenLeft;
            var vst = SystemParameters.VirtualScreenTop;
            var vsr = vsl + SystemParameters.VirtualScreenWidth;
            var vsb = vst + SystemParameters.VirtualScreenHeight;
            double dx = scale.DpiScaleX, dy = scale.DpiScaleY;

            // 取悬浮窗四周(紧贴边缘外侧)的小面片，避开窗内文字/图标像素
            int grabW = Math.Max(16, (int)Math.Round(24 * dx));
            int grabH = Math.Max(12, (int)Math.Round(18 * dy));
            var corners = new[]
            {
                (x: Left + 6,               y: Top + 4),
                (x: Left + Width - 8,       y: Top + 4),
                (x: Left + 6,               y: Top + Height - 10),
                (x: Left + Width - 8,       y: Top + Height - 10),
            };

            double sum = 0;
            int n = 0;
            foreach (var (x, y) in corners)
            {
                int sx = (int)Math.Round((x - vsl) * dx);
                int sy = (int)Math.Round((y - vst) * dy);
                int sxl = (int)Math.Round(vsl * dx);
                int syl = (int)Math.Round(vst * dy);
                int sxr = (int)Math.Round(vsr * dx);
                int syb = (int)Math.Round(vsb * dy);
                // 面片越出虚拟屏(贴边/贴角时可能没有足够桌面)则跳过
                if (sx < sxl || sy < syl || sx + grabW > sxr || sy + grabH > syb) continue;

                using var patch = new System.Drawing.Bitmap(grabW, grabH);
                using (var g = System.Drawing.Graphics.FromImage(patch))
                {
                    g.CopyFromScreen(sx, sy, 0, 0, new System.Drawing.Size(grabW, grabH),
                        System.Drawing.CopyPixelOperation.SourceCopy);
                }
                // 隔点采样，进一步降低取色开销
                for (int yy = 0; yy < grabH; yy += 2)
                    for (int xx = 0; xx < grabW; xx += 2)
                    {
                        var p = patch.GetPixel(xx, yy);
                        sum += 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
                        n++;
                    }
            }
            if (n == 0) return;   // 四周都采不到(极小屏/贴角)，保持上次状态

            double avg = sum / n;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
}