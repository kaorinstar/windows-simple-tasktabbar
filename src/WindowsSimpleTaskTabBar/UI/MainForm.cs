using System.Drawing.Drawing2D;
using Microsoft.Win32;
using WindowsSimpleTaskTabBar.Core.Layout;
using WindowsSimpleTaskTabBar.Core.Settings;
using WindowsSimpleTaskTabBar.Interop;
using WindowsSimpleTaskTabBar.Services;

namespace WindowsSimpleTaskTabBar.UI;

public class MainForm : Form
{
    // State of a single tab.
    private sealed class TabItem
    {
        public IntPtr Hwnd;
        public string Title = string.Empty;
        public Icon Icon;
        public Rectangle Bounds;
        public Rectangle CloseBounds;
        public bool Active;
    }

    // A window icon, with the time it was fetched. See IconMaxAgeMs.
    private sealed class CachedIcon
    {
        public Icon Icon;
        public int FetchedAt;
    }

    // Every drawing size now comes from BarMetrics, which derives them from the bar height
    // so that the compact height shrinks the contents with it.
    private AppSettings _settings = new();
    private BarMetrics _metrics = BarMetrics.For(
        AppSettings.HeightInPixels(BarHeightMode.Standard), 1.0f);

    // An application can change its icon while running, so a cached icon is fetched
    // again once it is older than this. Only one icon is refreshed per pass, because
    // WM_GETICON blocks until the owning window answers or the timeout expires.
    private const int IconMaxAgeMs = 10_000;

    private uint _callbackMessage;
    private bool _appBarRegistered;

    private readonly List<TabItem> _tabs = new();
    private readonly List<IntPtr> _order = new();          // keeps the display order stable
    private readonly Dictionary<IntPtr, CachedIcon> _iconCache = new();

    private readonly System.Windows.Forms.Timer _timer = new();
    private NativeMethods.WinEventDelegate _winEventProc;   // kept in a field so it is not collected
    private readonly List<IntPtr> _hooks = new();
    private bool _dirty = true;
    private int _tickCount;

    private int _hoverIndex = -1;
    private bool _hoverClose;

    private readonly ToolTip _toolTip = new();
    private string _toolTipText = string.Empty;

    private NotifyIcon _trayIcon;
    private SettingsForm _settingsForm;
    private IntPtr _trayIconHandle;   // owned by this class; see LoadSmallApplicationIcon

    private float _scale = 1.0f;
    private Font _font;

    // Colors, chosen to match the current Windows theme
    private Color _cBack, _cTab, _cTabActive, _cTabHover, _cText, _cTextActive, _cLine;

    public MainForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        Text = "WindowsSimpleTaskTabBar";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        _settings = SettingsStore.Load();

        ApplyTheme();

        _timer.Interval = 250;
        _timer.Tick += (_, __) => OnTimerTick();

        // Titles are drawn with an ellipsis, so the tooltip is the only way to read a
        // long one. ShowAlways is required because the bar is usually not the active window.
        _toolTip.ShowAlways = true;
        _toolTip.InitialDelay = 500;
        _toolTip.ReshowDelay = 200;
        _toolTip.AutoPopDelay = 10000;

        ContextMenuStrip = BuildMenu();
        CreateTrayIcon();
    }

    // ---------------------------------------------------------------
    // Menu and tray icon
    // ---------------------------------------------------------------

    /// <summary>
    /// Builds the application menu. The bar and the tray icon each need their own
    /// <see cref="ContextMenuStrip"/> instance, but both are built here so the two
    /// cannot drift apart.
    /// </summary>
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, __) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh", null, (_, __) => { _dirty = true; RefreshTabs(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, __) => Close());
        return menu;
    }

    /// <summary>
    /// Puts an icon in the notification area. Without it the only way to reach the menu is
    /// to right-click the bar itself, which is also where the tabs are.
    /// </summary>
    private void CreateTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = LoadSmallApplicationIcon(),
            Text = "WindowsSimpleTaskTabBar",
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
    }

    /// <summary>
    /// Reads the small icon back out of this executable, so the tray and the executable
    /// always show the same image and there is no second copy of it to keep in step.
    /// </summary>
    /// <remarks>
    /// <c>Icon.ExtractAssociatedIcon</c> would be shorter, but it only ever returns the
    /// large icon, which the notification area then shrinks. Asking Windows for the small
    /// icon picks the entry drawn for that size instead.
    /// </remarks>
    private Icon LoadSmallApplicationIcon()
    {
        try
        {
            var small = new IntPtr[1];
            if (NativeMethods.ExtractIconExW(Application.ExecutablePath, 0, null, small, 1) > 0
                && small[0] != IntPtr.Zero)
            {
                _trayIconHandle = small[0];
                // Icon.FromHandle does not take ownership, so the handle is destroyed on exit.
                return Icon.FromHandle(_trayIconHandle);
            }
        }
        catch
        {
            // Falls through to the system icon below.
        }

        return SystemIcons.Application;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= (int)NativeMethods.WS_EX_TOOLWINDOW;   // hide from the Alt+Tab list
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        uint dpi = NativeMethods.GetDpiForWindow(Handle);
        if (dpi == 0) dpi = 96;
        _scale = dpi / 96f;
        RebuildMetrics();

        RegisterAppBar();
        RegisterHooks();
        RefreshTabs();
        _timer.Start();
    }

    // ---------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------

    /// <summary>
    /// Recomputes every drawing size and rebuilds the font. Called whenever the bar height or
    /// the DPI changes, so the two can never disagree.
    /// </summary>
    private void RebuildMetrics()
    {
        _metrics = BarMetrics.For(AppSettings.HeightInPixels(_settings.BarHeight), _scale);

        _font?.Dispose();
        _font = new Font("Yu Gothic UI", _metrics.FontPixels, GraphicsUnit.Pixel);
    }

    /// <summary>
    /// Opens the settings dialog. Only one at a time, and changes take effect as they are made.
    /// </summary>
    private void ShowSettings()
    {
        if (_settingsForm != null)
        {
            _settingsForm.Activate();
            return;
        }

        using var form = new SettingsForm(_settings, ApplySettings);
        _settingsForm = form;
        try
        {
            form.ShowDialog();
        }
        finally
        {
            _settingsForm = null;
        }
    }

    /// <summary>
    /// Applies a settings change and writes it out. A failed write is not reported: losing a
    /// preference is not worth interrupting the user for.
    /// </summary>
    private void ApplySettings()
    {
        RebuildMetrics();
        UpdateAppBarPosition();   // the reserved area changes, so other windows resize with it
        LayoutTabs();
        Invalidate();

        SettingsStore.Save(_settings);
    }

    // ---------------------------------------------------------------
    // AppBar registration and positioning
    // ---------------------------------------------------------------
    private void RegisterAppBar()
    {
        _callbackMessage = NativeMethods.RegisterWindowMessage("WindowsSimpleTaskTabBar_AppBarMessage");

        var data = new NativeMethods.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = Handle,
            uCallbackMessage = _callbackMessage,
        };

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data);
        _appBarRegistered = true;
        UpdateAppBarPosition();
    }

    private void UnregisterAppBar()
    {
        if (!_appBarRegistered) return;

        var data = new NativeMethods.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = Handle,
        };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
        _appBarRegistered = false;
    }

    private void UpdateAppBarPosition()
    {
        if (!_appBarRegistered) return;

        Rectangle screen = Screen.PrimaryScreen.Bounds;
        int height = _metrics.BarHeight;

        var data = new NativeMethods.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = Handle,
            uEdge = NativeMethods.ABE_BOTTOM,
        };
        data.rc.left = screen.Left;
        data.rc.right = screen.Right;
        data.rc.top = screen.Bottom - height;
        data.rc.bottom = screen.Bottom;

        // Ask the system for free space at the bottom edge; this pushes the bar above the taskbar.
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);
        data.rc.top = data.rc.bottom - height;

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);

        NativeMethods.SetWindowPos(Handle, IntPtr.Zero,
            data.rc.left, data.rc.top,
            data.rc.right - data.rc.left, data.rc.bottom - data.rc.top,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    protected override void WndProc(ref Message m)
    {
        if (_callbackMessage != 0 && m.Msg == (int)_callbackMessage)
        {
            switch (m.WParam.ToInt32())
            {
                case NativeMethods.ABN_POSCHANGED:
                case NativeMethods.ABN_FULLSCREENAPP:
                    UpdateAppBarPosition();
                    break;
            }
        }
        else if (m.Msg == 0x007E /* WM_DISPLAYCHANGE */ || m.Msg == 0x02E0 /* WM_DPICHANGED */)
        {
            uint dpi = NativeMethods.GetDpiForWindow(Handle);
            if (dpi > 0)
            {
                _scale = dpi / 96f;
                RebuildMetrics();
            }
            UpdateAppBarPosition();
        }

        base.WndProc(ref m);
    }

    // ---------------------------------------------------------------
    // Detecting window changes
    // ---------------------------------------------------------------
    private void RegisterHooks()
    {
        _winEventProc = WinEventCallback;

        AddHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND);
        AddHook(NativeMethods.EVENT_OBJECT_CREATE, NativeMethods.EVENT_OBJECT_HIDE);
        AddHook(NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE);
    }

    private void AddHook(uint min, uint max)
    {
        IntPtr hook = NativeMethods.SetWinEventHook(min, max, IntPtr.Zero, _winEventProc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
    }

    private void WinEventCallback(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeMethods.OBJID_WINDOW) return;
        _dirty = true;   // the timer performs the actual refresh in batches
    }

    private void OnTimerTick()
    {
        _tickCount++;
        // Refresh on change, plus every two seconds as a safety net.
        if (_dirty || _tickCount % 8 == 0)
        {
            _dirty = false;
            RefreshTabs();
        }
    }

    // ---------------------------------------------------------------
    // Refreshing the tab list
    // ---------------------------------------------------------------
    private void RefreshTabs()
    {
        List<IntPtr> current = WindowService.EnumerateTaskWindows(Handle);
        IntPtr foreground = NativeMethods.GetForegroundWindow();

        // Keep the existing order and append newly opened windows at the end.
        // Membership is tested through sets: this runs every 250 ms.
        var live = new HashSet<IntPtr>(current);
        _order.RemoveAll(h => !live.Contains(h));

        var known = new HashSet<IntPtr>(_order);
        foreach (IntPtr h in current)
        {
            if (known.Add(h)) _order.Add(h);
        }

        // Release icons that are no longer needed.
        foreach (IntPtr key in _iconCache.Keys.ToList())
        {
            if (!known.Contains(key))
            {
                _iconCache[key].Icon?.Dispose();
                _iconCache.Remove(key);
            }
        }

        // Before the tabs are rebuilt, so no tab holds an icon that is about to be replaced.
        RefreshStalestIcon();

        _tabs.Clear();
        foreach (IntPtr h in _order)
        {
            if (!_iconCache.TryGetValue(h, out CachedIcon cached))
            {
                cached = new CachedIcon
                {
                    Icon = WindowService.GetWindowIcon(h),
                    FetchedAt = Environment.TickCount,
                };
                _iconCache[h] = cached;
            }

            _tabs.Add(new TabItem
            {
                Hwnd = h,
                Title = WindowService.GetTitle(h),
                Icon = cached.Icon,
                Active = (h == foreground),
            });
        }

        LayoutTabs();

        // The tab list has just been rebuilt, so a stored hover index would now point at
        // a different window. Take it from where the pointer actually is.
        RecomputeHover();
        UpdateToolTip();

        Invalidate();
    }

    /// <summary>
    /// Fetches the single oldest cached icon again, if it has passed <see cref="IconMaxAgeMs"/>.
    /// Only one per pass: the underlying WM_GETICON call blocks until the owning window
    /// answers or times out, and this runs on the UI thread.
    /// </summary>
    private void RefreshStalestIcon()
    {
        int now = Environment.TickCount;
        IntPtr stalest = IntPtr.Zero;
        int oldest = IconMaxAgeMs;

        foreach (IntPtr h in _order)
        {
            if (!_iconCache.TryGetValue(h, out CachedIcon cached)) continue;

            int age = unchecked(now - cached.FetchedAt);   // correct across TickCount wrapping
            if (age >= oldest)
            {
                oldest = age;
                stalest = h;
            }
        }

        if (stalest == IntPtr.Zero) return;

        CachedIcon entry = _iconCache[stalest];
        Icon previous = entry.Icon;
        entry.Icon = WindowService.GetWindowIcon(stalest);
        entry.FetchedAt = Environment.TickCount;
        previous?.Dispose();
    }

    private void LayoutTabs()
    {
        if (_tabs.Count == 0) return;

        int margin = _metrics.OuterMargin;
        int available = ClientSize.Width - margin * 2;
        int width = TabLayout.CalculateTabWidth(
            available, _tabs.Count, _metrics.TabGap, _metrics.TabMinWidth, _metrics.TabMaxWidth);

        int x = margin;
        int top = _metrics.TopOffset;
        int height = ClientSize.Height - top;

        foreach (TabItem tab in _tabs)
        {
            tab.Bounds = new Rectangle(x, top, width, height);

            int closeSize = _metrics.CloseButtonSize;
            tab.CloseBounds = width > _metrics.CloseButtonMinTabWidth
                ? new Rectangle(x + width - closeSize - _metrics.SmallGap,
                                top + (height - closeSize) / 2, closeSize, closeSize)
                : Rectangle.Empty;

            x += width + _metrics.TabGap;
        }
    }

    // ---------------------------------------------------------------
    // Painting
    // ---------------------------------------------------------------
    private void ApplyTheme()
    {
        bool light = IsLightTheme();
        if (light)
        {
            _cBack = Color.FromArgb(242, 243, 245);
            _cTab = Color.FromArgb(226, 228, 232);
            _cTabHover = Color.FromArgb(235, 237, 240);
            _cTabActive = Color.FromArgb(255, 255, 255);
            _cText = Color.FromArgb(70, 74, 80);
            _cTextActive = Color.FromArgb(24, 26, 30);
            _cLine = Color.FromArgb(210, 213, 218);
        }
        else
        {
            _cBack = Color.FromArgb(32, 33, 36);
            _cTab = Color.FromArgb(48, 50, 54);
            _cTabHover = Color.FromArgb(58, 61, 66);
            _cTabActive = Color.FromArgb(78, 82, 88);
            _cText = Color.FromArgb(178, 182, 188);
            _cTextActive = Color.FromArgb(245, 246, 248);
            _cLine = Color.FromArgb(24, 25, 28);
        }
        BackColor = _cBack;
    }

    private static bool IsLightTheme()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i != 0;
        }
        catch
        {
            return false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(_cBack);

        using (var pen = new Pen(_cLine))
            g.DrawLine(pen, 0, 0, ClientSize.Width, 0);

        if (_tabs.Count == 0)
        {
            // Centred in the bar rather than placed at a fixed offset, so it stays put when
            // the bar height changes.
            var emptyRect = new Rectangle(
                _metrics.Padding, _metrics.TopOffset,
                ClientSize.Width - _metrics.Padding * 2, ClientSize.Height - _metrics.TopOffset);
            TextRenderer.DrawText(g, "No windows to show", _font, emptyRect, _cText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        int radius = _metrics.CornerRadius;
        int visible = VisibleTabCount();

        for (int i = 0; i < visible; i++)
        {
            TabItem tab = _tabs[i];

            Color fill = tab.Active ? _cTabActive : (i == _hoverIndex ? _cTabHover : _cTab);
            using (GraphicsPath path = RoundedTop(tab.Bounds, radius))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            int padding = _metrics.Padding;
            int iconSize = _metrics.IconSize;
            int textLeft = tab.Bounds.Left + padding;

            if (tab.Icon != null)
            {
                var iconRect = new Rectangle(tab.Bounds.Left + padding,
                    tab.Bounds.Top + (tab.Bounds.Height - iconSize) / 2, iconSize, iconSize);
                g.DrawIcon(tab.Icon, iconRect);
                textLeft = iconRect.Right + _metrics.SmallGap;
            }

            int textRight = tab.CloseBounds.IsEmpty
                ? tab.Bounds.Right - padding
                : tab.CloseBounds.Left - _metrics.OuterMargin;

            if (textRight > textLeft)
            {
                var textRect = new Rectangle(textLeft, tab.Bounds.Top,
                    textRight - textLeft, tab.Bounds.Height);
                TextRenderer.DrawText(g, tab.Title, _font, textRect,
                    tab.Active ? _cTextActive : _cText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            if (!tab.CloseBounds.IsEmpty && (i == _hoverIndex || tab.Active))
            {
                Color color = (i == _hoverIndex && _hoverClose) ? Color.FromArgb(232, 74, 74) : _cText;
                using var pen = new Pen(color, Math.Max(1f, 1.3f * _scale));
                Rectangle c = tab.CloseBounds;
                int inset = _metrics.CloseButtonSize / 4;
                g.DrawLine(pen, c.Left + inset, c.Top + inset, c.Right - inset, c.Bottom - inset);
                g.DrawLine(pen, c.Right - inset, c.Top + inset, c.Left + inset, c.Bottom - inset);
            }
        }
    }

    private static GraphicsPath RoundedTop(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        path.CloseFigure();
        return path;
    }

    // ---------------------------------------------------------------
    // Mouse input
    // ---------------------------------------------------------------
    /// <summary>
    /// How many tabs fit in the bar. Tabs past this point are not drawn, so painting and
    /// hit testing both stop here; otherwise a click could reach a tab that is not on screen.
    /// </summary>
    private int VisibleTabCount()
    {
        int count = 0;
        while (count < _tabs.Count && _tabs[count].Bounds.Right <= ClientSize.Width)
            count++;
        return count;
    }

    private int HitTest(Point p, out bool onClose)
    {
        onClose = false;
        int visible = VisibleTabCount();

        for (int i = 0; i < visible; i++)
        {
            if (_tabs[i].Bounds.Contains(p))
            {
                onClose = !_tabs[i].CloseBounds.IsEmpty && _tabs[i].CloseBounds.Contains(p);
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Recomputes the hovered tab from the pointer's current position.
    /// </summary>
    private void RecomputeHover()
    {
        Point p = PointToClient(MousePosition);

        if (ClientRectangle.Contains(p))
        {
            _hoverIndex = HitTest(p, out _hoverClose);
        }
        else
        {
            _hoverIndex = -1;
            _hoverClose = false;
        }
    }

    private void SetHover(int index, bool onClose)
    {
        if (index == _hoverIndex && onClose == _hoverClose) return;

        _hoverIndex = index;
        _hoverClose = onClose;
        UpdateToolTip();
        Invalidate();
    }

    /// <summary>
    /// Shows the full window title of the hovered tab. Titles are drawn with an ellipsis,
    /// so this is the only way to read one that does not fit.
    /// </summary>
    private void UpdateToolTip()
    {
        string text = _hoverIndex >= 0 && _hoverIndex < _tabs.Count
            ? _tabs[_hoverIndex].Title
            : string.Empty;

        // Setting the same text again restarts the tooltip and makes it flicker.
        if (text == _toolTipText) return;

        _toolTipText = text;
        _toolTip.SetToolTip(this, text);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location, out bool onClose);
        SetHover(index, onClose);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1, false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = HitTest(e.Location, out bool onClose);
        if (index < 0) return;

        TabItem tab = _tabs[index];

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && onClose))
        {
            WindowService.Close(tab.Hwnd);
            _dirty = true;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            // Clicking the active tab minimizes it, the same as the taskbar does.
            if (tab.Active && !NativeMethods.IsIconic(tab.Hwnd))
                WindowService.Minimize(tab.Hwnd);
            else
                WindowService.Activate(tab.Hwnd);

            _dirty = true;
        }
    }

    // ---------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();

        foreach (IntPtr hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();

        foreach (CachedIcon cached in _iconCache.Values)
            cached.Icon?.Dispose();
        _iconCache.Clear();

        _toolTip.Dispose();

        // Hide before disposing. A tray icon that is only disposed can be left behind as a
        // dead entry in the notification area until the user hovers over it.
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();   // the NotifyIcon does not own it
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_trayIconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }

        UnregisterAppBar();
        base.OnFormClosing(e);
    }
}
