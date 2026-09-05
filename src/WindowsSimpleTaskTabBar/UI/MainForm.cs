using System.Drawing.Drawing2D;
using Microsoft.Win32;
using WindowsSimpleTaskTabBar.Core.Layout;
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

    private const int BarHeightLogical = 34;
    private const int TabMaxWidthLogical = 220;
    private const int TabMinWidthLogical = 46;
    private const int TabGapLogical = 2;

    private uint _callbackMessage;
    private bool _appBarRegistered;

    private readonly List<TabItem> _tabs = new();
    private readonly List<IntPtr> _order = new();          // keeps the display order stable
    private readonly Dictionary<IntPtr, Icon> _iconCache = new();

    private readonly System.Windows.Forms.Timer _timer = new();
    private NativeMethods.WinEventDelegate _winEventProc;   // kept in a field so it is not collected
    private readonly List<IntPtr> _hooks = new();
    private bool _dirty = true;
    private int _tickCount;

    private int _hoverIndex = -1;
    private bool _hoverClose;

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

        ApplyTheme();

        _timer.Interval = 250;
        _timer.Tick += (_, __) => OnTimerTick();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, (_, __) => { _dirty = true; RefreshTabs(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, __) => Close());
        ContextMenuStrip = menu;
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
        _font = new Font("Yu Gothic UI", 12f * _scale, GraphicsUnit.Pixel);

        RegisterAppBar();
        RegisterHooks();
        RefreshTabs();
        _timer.Start();
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
        int height = (int)Math.Round(BarHeightLogical * _scale);

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
                _font?.Dispose();
                _font = new Font("Yu Gothic UI", 12f * _scale, GraphicsUnit.Pixel);
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
        _order.RemoveAll(h => !current.Contains(h));
        foreach (IntPtr h in current)
        {
            if (!_order.Contains(h)) _order.Add(h);
        }

        // Release icons that are no longer needed.
        foreach (IntPtr key in _iconCache.Keys.ToList())
        {
            if (!_order.Contains(key))
            {
                _iconCache[key]?.Dispose();
                _iconCache.Remove(key);
            }
        }

        _tabs.Clear();
        foreach (IntPtr h in _order)
        {
            if (!_iconCache.TryGetValue(h, out Icon icon))
            {
                icon = WindowService.GetWindowIcon(h);
                _iconCache[h] = icon;
            }

            _tabs.Add(new TabItem
            {
                Hwnd = h,
                Title = WindowService.GetTitle(h),
                Icon = icon,
                Active = (h == foreground),
            });
        }

        LayoutTabs();
        Invalidate();
    }

    private void LayoutTabs()
    {
        if (_tabs.Count == 0) return;

        int gap = (int)Math.Round(TabGapLogical * _scale);
        int maxW = (int)Math.Round(TabMaxWidthLogical * _scale);
        int minW = (int)Math.Round(TabMinWidthLogical * _scale);
        int margin = (int)Math.Round(4 * _scale);

        int available = ClientSize.Width - margin * 2;
        int width = TabLayout.CalculateTabWidth(available, _tabs.Count, gap, minW, maxW);

        int x = margin;
        int top = (int)Math.Round(3 * _scale);
        int height = ClientSize.Height - top;

        foreach (TabItem tab in _tabs)
        {
            tab.Bounds = new Rectangle(x, top, width, height);

            int closeSize = (int)Math.Round(16 * _scale);
            tab.CloseBounds = width > (int)Math.Round(90 * _scale)
                ? new Rectangle(x + width - closeSize - (int)Math.Round(6 * _scale),
                                top + (height - closeSize) / 2, closeSize, closeSize)
                : Rectangle.Empty;

            x += width + gap;
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
            TextRenderer.DrawText(g, "No windows to show", _font,
                new Point((int)(8 * _scale), (int)(9 * _scale)), _cText);
            return;
        }

        int radius = (int)Math.Round(6 * _scale);

        for (int i = 0; i < _tabs.Count; i++)
        {
            TabItem tab = _tabs[i];
            if (tab.Bounds.Right > ClientSize.Width) break;   // skip tabs that do not fit

            Color fill = tab.Active ? _cTabActive : (i == _hoverIndex ? _cTabHover : _cTab);
            using (GraphicsPath path = RoundedTop(tab.Bounds, radius))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            int padding = (int)Math.Round(8 * _scale);
            int iconSize = (int)Math.Round(16 * _scale);
            int textLeft = tab.Bounds.Left + padding;

            if (tab.Icon != null)
            {
                var iconRect = new Rectangle(tab.Bounds.Left + padding,
                    tab.Bounds.Top + (tab.Bounds.Height - iconSize) / 2, iconSize, iconSize);
                g.DrawIcon(tab.Icon, iconRect);
                textLeft = iconRect.Right + (int)Math.Round(6 * _scale);
            }

            int textRight = tab.CloseBounds.IsEmpty
                ? tab.Bounds.Right - padding
                : tab.CloseBounds.Left - (int)Math.Round(4 * _scale);

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
                int inset = (int)Math.Round(4 * _scale);
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
    private int HitTest(Point p, out bool onClose)
    {
        onClose = false;
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Bounds.Contains(p))
            {
                onClose = !_tabs[i].CloseBounds.IsEmpty && _tabs[i].CloseBounds.Contains(p);
                return i;
            }
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location, out bool onClose);
        if (index != _hoverIndex || onClose != _hoverClose)
        {
            _hoverIndex = index;
            _hoverClose = onClose;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            _hoverClose = false;
            Invalidate();
        }
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

        foreach (Icon icon in _iconCache.Values)
            icon?.Dispose();
        _iconCache.Clear();

        UnregisterAppBar();
        base.OnFormClosing(e);
    }
}
