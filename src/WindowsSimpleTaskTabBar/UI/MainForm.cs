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

    // Horizontal scrolling, used once the tabs have shrunk as far as they are allowed to.
    private TabStripLayout _strip = new();
    private int _scroll;
    private Rectangle _contentRect;          // where tabs are drawn, between the arrows
    private Rectangle _scrollLeftButton;
    private Rectangle _scrollRightButton;
    private int _hoverButton = -1;           // 0 left arrow, 1 right arrow, -1 neither
    private IntPtr _lastForeground;

    // Dragging a tab to a new position. A press is only a candidate for a drag: what it turns
    // out to be is decided on release, so a press that does not move still acts as a click.
    private IntPtr _pressedHwnd;             // the tab the left button went down on
    private Point _pressOrigin;
    private bool _dragging;
    private IntPtr _dragHwnd;
    private int _dragGrabOffset;             // pointer x minus the tab's left edge, at the press
    private int _dragX;                      // pointer x now
    private int _dragStartIndex = -1;        // where the dragged tab was when the drag began
    private List<IntPtr> _orderBeforeDrag;   // to put back if the drag is cancelled
    private int _lastDragScroll;             // TickCount of the last drag-driven scroll

    /// <summary>How often the row scrolls while a tab is dragged against either end.</summary>
    private const int DragScrollIntervalMs = 120;

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
        else if (m.Msg == 0x020A /* WM_MOUSEWHEEL */)
        {
            // Handled here rather than through OnMouseWheel, which only fires for the focused
            // control. The bar is usually not focused; Windows still delivers the message when
            // "scroll inactive windows when I hover over them" is on, which it is by default.
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            if (delta != 0) ScrollBy(delta > 0 ? -1 : 1);
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

        // A window closed mid-drag takes the drag with it: there is nothing left to move.
        if (_dragging && !_order.Contains(_dragHwnd)) EndDrag();

        LayoutTabs();

        // Not while dragging: the row must stay where the user is working, whatever gains focus.
        if (!_dragging) ScrollToForegroundTab(foreground);

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
        int margin = _metrics.OuterMargin;
        int top = _metrics.TopOffset;
        int height = ClientSize.Height - top;
        int gap = _metrics.TabGap;

        if (_tabs.Count == 0)
        {
            _strip = new TabStripLayout();
            _scroll = 0;
            _contentRect = new Rectangle(margin, top, ClientSize.Width - margin * 2, height);
            _scrollLeftButton = Rectangle.Empty;
            _scrollRightButton = Rectangle.Empty;
            return;
        }

        // Measured twice on purpose. The arrows only appear when the row scrolls, and they take
        // width away from the row, so the second pass measures against what is actually left.
        // Narrowing the space can only make scrolling more likely, so this settles in two passes.
        int available = ClientSize.Width - margin * 2;
        _strip = TabStrip.Measure(available, _tabs.Count, gap,
            _metrics.TabMinWidth, _metrics.TabMaxWidth);

        int contentLeft = margin;
        if (_strip.CanScroll)
        {
            int button = _metrics.ScrollButtonWidth;
            contentLeft = margin + button;
            available -= button * 2;

            _strip = TabStrip.Measure(available, _tabs.Count, gap,
                _metrics.TabMinWidth, _metrics.TabMaxWidth);

            _scrollLeftButton = new Rectangle(margin, top, button, height);
            _scrollRightButton = new Rectangle(
                ClientSize.Width - margin - button, top, button, height);
        }
        else
        {
            _scrollLeftButton = Rectangle.Empty;
            _scrollRightButton = Rectangle.Empty;
        }

        _contentRect = new Rectangle(contentLeft, top, available, height);
        _scroll = TabStrip.ClampScroll(_scroll, _strip.MaxScroll);

        int width = _strip.TabWidth;
        int x = contentLeft - _scroll;

        foreach (TabItem tab in _tabs)
        {
            tab.Bounds = new Rectangle(x, top, width, height);

            // A narrow tab gives its close button up to the title.
            int closeSize = _metrics.CloseButtonSize;
            tab.CloseBounds = width > _metrics.CloseButtonMinTabWidth
                ? new Rectangle(x + width - closeSize - _metrics.SmallGap,
                                top + (height - closeSize) / 2, closeSize, closeSize)
                : Rectangle.Empty;

            x += width + gap;
        }

        if (_dragging) PositionDraggedTab();
    }

    /// <summary>
    /// Puts the dragged tab under the pointer, where the tabs it has displaced are already
    /// drawn around it. It stays inside the row, so it never covers the scroll arrows.
    /// </summary>
    private void PositionDraggedTab()
    {
        int index = _tabs.FindIndex(t => t.Hwnd == _dragHwnd);
        if (index < 0) return;

        TabItem tab = _tabs[index];
        int left = DraggedTabLeft(tab.Bounds.Width);
        int shift = left - tab.Bounds.Left;
        if (shift == 0) return;

        tab.Bounds = new Rectangle(left, tab.Bounds.Top, tab.Bounds.Width, tab.Bounds.Height);
        if (!tab.CloseBounds.IsEmpty)
        {
            tab.CloseBounds = new Rectangle(
                tab.CloseBounds.Left + shift, tab.CloseBounds.Top,
                tab.CloseBounds.Width, tab.CloseBounds.Height);
        }
    }

    /// <summary>
    /// Where the dragged tab's left edge sits: under the pointer, held inside the row. Drawing
    /// and the drop position both come from this, so what is seen is what is dropped.
    /// </summary>
    private int DraggedTabLeft(int width)
    {
        int left = _dragX - _dragGrabOffset;
        int rightMost = _contentRect.Right - width;

        if (left > rightMost) left = rightMost;
        if (left < _contentRect.Left) left = _contentRect.Left;
        return left;
    }

    /// <summary>
    /// Brings the newly activated window's tab into view. Only when the foreground window has
    /// actually changed, so the row does not jump away from wherever the user scrolled it to.
    /// </summary>
    private void ScrollToForegroundTab(IntPtr foreground)
    {
        if (foreground == _lastForeground) return;
        _lastForeground = foreground;

        if (!_strip.CanScroll) return;

        int index = _tabs.FindIndex(t => t.Hwnd == foreground);
        if (index < 0) return;

        int scrolled = TabStrip.ScrollToShow(index, _strip.TabWidth, _metrics.TabGap,
            _contentRect.Width, _scroll, _strip.MaxScroll);
        if (scrolled == _scroll) return;

        _scroll = scrolled;
        LayoutTabs();
    }

    /// <summary>
    /// Moves the row by a number of tabs. Positive scrolls towards the end.
    /// </summary>
    private void ScrollBy(int tabs)
    {
        if (!_strip.CanScroll || tabs == 0) return;

        int step = (_strip.TabWidth + _metrics.TabGap) * tabs;
        int scrolled = TabStrip.ClampScroll(_scroll + step, _strip.MaxScroll);
        if (scrolled == _scroll) return;

        _scroll = scrolled;
        LayoutTabs();
        RecomputeHover();
        UpdateToolTip();
        Invalidate();
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

        // Clipped, so a partly scrolled tab stops at the edge of the row instead of painting
        // over the arrows.
        g.SetClip(_contentRect);

        int dragIndex = _dragging ? _tabs.FindIndex(t => t.Hwnd == _dragHwnd) : -1;

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (i == dragIndex) continue;
            if (IsTabVisible(_tabs[i])) DrawTab(g, _tabs[i], i);
        }

        // The dragged tab goes last, so it passes over its neighbours rather than under them.
        if (dragIndex >= 0 && IsTabVisible(_tabs[dragIndex]))
            DrawTab(g, _tabs[dragIndex], dragIndex);

        g.ResetClip();
        DrawScrollButtons(g);
    }

    private void DrawTab(Graphics g, TabItem tab, int index)
    {
        Color fill = tab.Active ? _cTabActive : (index == _hoverIndex ? _cTabHover : _cTab);
        using (GraphicsPath path = RoundedTop(tab.Bounds, _metrics.CornerRadius))
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
            // NoPadding matters here. Without it TextRenderer keeps a few pixels at each end
            // for itself, which at the narrowest tab width costs a character of the title.
            TextRenderer.DrawText(g, tab.Title, _font, textRect,
                tab.Active ? _cTextActive : _cText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                | TextFormatFlags.NoPadding);
        }

        if (!tab.CloseBounds.IsEmpty && (index == _hoverIndex || tab.Active))
        {
            Color color = (index == _hoverIndex && _hoverClose)
                ? Color.FromArgb(232, 74, 74)
                : _cText;
            using var pen = new Pen(color, Math.Max(1f, 1.3f * _scale));
            Rectangle c = tab.CloseBounds;
            int inset = _metrics.CloseButtonSize / 4;
            g.DrawLine(pen, c.Left + inset, c.Top + inset, c.Right - inset, c.Bottom - inset);
            g.DrawLine(pen, c.Right - inset, c.Top + inset, c.Left + inset, c.Bottom - inset);
        }
    }

    /// <summary>
    /// Draws the two arrows that scroll the row. The one pointing at an end the row has already
    /// reached is dimmed, so it is clear which way there is still something to see.
    /// </summary>
    private void DrawScrollButtons(Graphics g)
    {
        if (!_strip.CanScroll) return;

        DrawScrollButton(g, _scrollLeftButton, pointsLeft: true,
            enabled: _scroll > 0, hovered: _hoverButton == 0);
        DrawScrollButton(g, _scrollRightButton, pointsLeft: false,
            enabled: _scroll < _strip.MaxScroll, hovered: _hoverButton == 1);
    }

    private void DrawScrollButton(Graphics g, Rectangle box, bool pointsLeft, bool enabled, bool hovered)
    {
        if (box.IsEmpty) return;

        if (hovered && enabled)
        {
            using var brush = new SolidBrush(_cTabHover);
            g.FillRectangle(brush, box);
        }

        Color color = enabled ? _cText : Color.FromArgb(90, _cText);
        int arm = Math.Max(2, _metrics.IconSize / 4);
        int cx = box.Left + box.Width / 2;
        int cy = box.Top + box.Height / 2;
        int tip = pointsLeft ? cx - arm / 2 : cx + arm / 2;
        int tail = pointsLeft ? cx + arm / 2 : cx - arm / 2;

        using var pen = new Pen(color, Math.Max(1f, 1.3f * _scale));
        g.DrawLine(pen, tail, cy - arm, tip, cy);
        g.DrawLine(pen, tip, cy, tail, cy + arm);
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
    /// Whether any part of a tab falls inside the row. Painting and hit testing share this, so
    /// a click can never reach a tab that is scrolled out of sight.
    /// </summary>
    private bool IsTabVisible(TabItem tab)
    {
        return tab.Bounds.IntersectsWith(_contentRect);
    }

    private int HitTest(Point p, out bool onClose)
    {
        onClose = false;

        // Outside the row means the arrows or the margins, not a tab.
        if (!_contentRect.Contains(p)) return -1;

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (!IsTabVisible(_tabs[i])) continue;

            if (_tabs[i].Bounds.Contains(p))
            {
                onClose = !_tabs[i].CloseBounds.IsEmpty && _tabs[i].CloseBounds.Contains(p);
                return i;
            }
        }
        return -1;
    }

    /// <summary>Which scroll arrow the point falls on, or -1 for neither.</summary>
    private int HitTestScrollButton(Point p)
    {
        if (!_strip.CanScroll) return -1;
        if (_scrollLeftButton.Contains(p)) return 0;
        if (_scrollRightButton.Contains(p)) return 1;
        return -1;
    }

    /// <summary>
    /// Recomputes the hovered tab from the pointer's current position.
    /// </summary>
    private void RecomputeHover()
    {
        // A tooltip or a highlight following the pointer during a drag only gets in the way.
        if (_dragging)
        {
            _hoverIndex = -1;
            _hoverClose = false;
            return;
        }

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

        if (_dragging)
        {
            UpdateDrag(e.X);
            return;
        }

        if (_pressedHwnd != IntPtr.Zero)
        {
            // The button may have been released over another window, which never reaches here.
            if ((e.Button & MouseButtons.Left) == 0) EndPress();
            else if (HasMovedFarEnoughToDrag(e.Location)) BeginDrag(e.X);
            return;
        }

        int button = HitTestScrollButton(e.Location);
        if (button != _hoverButton)
        {
            _hoverButton = button;
            Invalidate();
        }

        int index = HitTest(e.Location, out bool onClose);
        SetHover(index, onClose);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverButton != -1)
        {
            _hoverButton = -1;
            Invalidate();
        }

        SetHover(-1, false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            int button = HitTestScrollButton(e.Location);
            if (button >= 0)
            {
                ScrollBy(button == 0 ? -1 : 1);
                return;
            }
        }

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
            // Nothing happens yet. The same press may turn into a drag, so what to do with it
            // is decided on release: a press that does not move still acts exactly as a click.
            _pressedHwnd = tab.Hwnd;
            _pressOrigin = e.Location;
            _dragGrabOffset = e.X - tab.Bounds.Left;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left) return;

        if (_dragging)
        {
            // Dropped where it was dragged to. The order is already the one on screen.
            EndDrag(treatUnmovedAsClick: true);
            return;
        }

        IntPtr pressed = _pressedHwnd;
        EndPress();
        if (pressed == IntPtr.Zero) return;

        // Released somewhere else, so this was not a click on that tab.
        int index = HitTest(e.Location, out bool onClose);
        if (index < 0 || onClose || _tabs[index].Hwnd != pressed) return;

        ClickTab(_tabs[index]);
    }

    /// <summary>
    /// What a click on a tab does: brings the window forward, or minimizes it if it is already
    /// the one at the front, the same as the taskbar does.
    /// </summary>
    private void ClickTab(TabItem tab)
    {
        if (tab.Active && !NativeMethods.IsIconic(tab.Hwnd))
            WindowService.Minimize(tab.Hwnd);
        else
            WindowService.Activate(tab.Hwnd);

        _dirty = true;
    }

    /// <summary>
    /// Whether the pointer has moved far enough from the press for this to be a drag rather
    /// than a click. Only sideways movement counts: a tab can only be moved along the row, and
    /// treating an up or down twitch as a drag would make ordinary clicks feel unreliable.
    /// </summary>
    private bool HasMovedFarEnoughToDrag(Point p)
    {
        // DragSize is the whole box the pointer may wander in, centred on the press.
        return Math.Abs(p.X - _pressOrigin.X) > SystemInformation.DragSize.Width / 2;
    }

    private void BeginDrag(int x)
    {
        _dragging = true;
        _dragHwnd = _pressedHwnd;
        _dragStartIndex = _order.IndexOf(_dragHwnd);
        _orderBeforeDrag = new List<IntPtr>(_order);
        _lastDragScroll = Environment.TickCount;

        SetHover(-1, false);
        UpdateDrag(x);
    }

    /// <summary>
    /// Follows the pointer: moves the dragged tab in the order as soon as it passes a
    /// neighbour, and scrolls the row when the tab is pushed against either end.
    /// </summary>
    private void UpdateDrag(int x)
    {
        _dragX = x;

        int index = _tabs.FindIndex(t => t.Hwnd == _dragHwnd);
        if (index < 0)
        {
            EndDrag();
            return;
        }

        DragScroll();

        int offset = DraggedTabLeft(_strip.TabWidth) - _contentRect.Left + _scroll;
        int target = TabStrip.DropIndex(offset, _strip.TabWidth, _metrics.TabGap, _tabs.Count);

        if (target != index)
        {
            // Both lists carry the same order: _tabs is what is drawn now, _order is what
            // survives the next refresh.
            TabStrip.Move(_order, index, target);
            TabStrip.Move(_tabs, index, target);
        }

        LayoutTabs();
        Invalidate();
    }

    /// <summary>
    /// Scrolls the row while the dragged tab is held against one of its ends, so a tab can be
    /// moved to a position that is not on screen at the time. Rate limited, because mouse moves
    /// arrive far faster than a row this can be read at.
    /// </summary>
    private void DragScroll()
    {
        if (!_strip.CanScroll) return;

        int direction = 0;
        if (_dragX <= _contentRect.Left) direction = -1;
        else if (_dragX >= _contentRect.Right) direction = 1;
        if (direction == 0) return;

        int now = Environment.TickCount;
        if (unchecked(now - _lastDragScroll) < DragScrollIntervalMs) return;

        _lastDragScroll = now;
        ScrollBy(direction);
    }

    /// <summary>Ends a drag, keeping the order it arrived at.</summary>
    /// <param name="treatUnmovedAsClick">
    /// Whether a tab dropped back in the slot it started in counts as a click. A drag begins
    /// after only a couple of pixels of movement, which an ordinary click can easily produce,
    /// and a click that did nothing at all would feel broken. Nothing moved on screen, so
    /// acting on it as a click is what the user saw happen.
    /// </param>
    private void EndDrag(bool treatUnmovedAsClick = false)
    {
        if (!_dragging) return;

        int index = _tabs.FindIndex(t => t.Hwnd == _dragHwnd);
        bool wasAClick = treatUnmovedAsClick && index >= 0 && index == _dragStartIndex;
        TabItem dropped = wasAClick ? _tabs[index] : null;

        _dragging = false;
        _dragHwnd = IntPtr.Zero;
        _dragStartIndex = -1;
        _orderBeforeDrag = null;
        EndPress();

        if (wasAClick) ClickTab(dropped);

        LayoutTabs();
        RecomputeHover();
        UpdateToolTip();
        Invalidate();
    }

    /// <summary>
    /// Abandons a drag and puts the order back as it was. Windows that opened during the drag
    /// keep their places at the end; windows that closed are simply gone.
    /// </summary>
    private void CancelDrag()
    {
        if (!_dragging) return;

        if (_orderBeforeDrag != null)
        {
            var before = new HashSet<IntPtr>(_orderBeforeDrag);
            var live = new HashSet<IntPtr>(_order);

            var restored = _orderBeforeDrag.Where(h => live.Contains(h)).ToList();
            restored.AddRange(_order.Where(h => !before.Contains(h)));

            _order.Clear();
            _order.AddRange(restored);
        }

        EndDrag();
        RefreshTabs();
    }

    private void EndPress()
    {
        _pressedHwnd = IntPtr.Zero;
        Capture = false;
    }

    /// <summary>
    /// Esc abandons a drag, as it does in a file manager. The bar takes focus when it is
    /// clicked, so a drag in progress is exactly when this key can reach it.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_dragging && keyData == Keys.Escape)
        {
            CancelDrag();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Losing the mouse capture ends the drag. Another window taking over the pointer means no
    /// further movement is seen, so carrying on would leave a tab stuck to a pointer that has
    /// gone elsewhere.
    /// </summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        if (Capture) return;
        if (_dragging) CancelDrag();
        else _pressedHwnd = IntPtr.Zero;
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
