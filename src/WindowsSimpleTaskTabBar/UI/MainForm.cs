using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WindowsSimpleTaskTabBar.Core.Grouping;
using WindowsSimpleTaskTabBar.Core.Layout;
using WindowsSimpleTaskTabBar.Core.Localization;
using WindowsSimpleTaskTabBar.Core.Ordering;
using WindowsSimpleTaskTabBar.Core.Preview;
using WindowsSimpleTaskTabBar.Core.Settings;
using WindowsSimpleTaskTabBar.Core.Theme;
using WindowsSimpleTaskTabBar.Interop;
using WindowsSimpleTaskTabBar.Services;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// One bar: the row of tabs on one monitor.
/// </summary>
/// <remarks>
/// This was the application as well until a bar was needed on every monitor (#62). Everything
/// there is one of - the settings, the tray icon, the caches, the event hooks and the timer -
/// now lives in <see cref="BarHost"/>, which creates one of these per monitor and hands each
/// one the windows that are on its own monitor.
///
/// What is left is a screen's worth of state: which windows this bar lists, the order they are
/// in, where the row is scrolled to, the sizes for this monitor's scale factor, and the AppBar
/// registration that keeps this monitor's windows off the strip the bar occupies.
/// </remarks>
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

        // Which group the tab belongs to, when grouping is on. GroupId is empty when it is off
        // and when the owning executable could not be read; Marked is false for a group of one,
        // where an accent would have nothing to say.
        public string GroupId = string.Empty;
        public int Accent = -1;
        public bool Marked;
    }

    /// <summary>The application, which owns everything there is one of.</summary>
    private readonly BarHost _host;

    // Every drawing size now comes from BarMetrics, which derives them from the bar height
    // so that the compact height shrinks the contents with it.
    private readonly AppSettings _settings;
    private BarMetrics _metrics = BarMetrics.For(
        AppSettings.HeightInPixels(BarHeightMode.Standard), 1.0f);

    private uint _callbackMessage;
    private bool _appBarRegistered;

    /// <summary>
    /// Which edge the bar is drawn against now. Settled by the setting and, when that says to
    /// follow the taskbar, by where the taskbar is; every size and every rounded corner below
    /// reads it rather than assuming the bottom.
    /// </summary>
    private ScreenEdge _edge = ScreenEdge.Bottom;

    /// <summary>Whether the bar sits on the top edge, which mirrors everything it draws.</summary>
    private bool AtTop => _edge == ScreenEdge.Top;

    private bool _released;               // see ReleaseResources

    private readonly List<TabItem> _tabs = new();
    private readonly List<IntPtr> _order = new();          // keeps the display order stable

    // Shared with every other bar. The executable behind a window is the same answer wherever
    // the window is, and asking once is what keeps the 250 ms refresh to a dictionary lookup.
    private readonly ProcessInfoCache _processInfo;

    // Whether the whole row still has to be put into the user's priority order. The bar starts
    // with the whole row to arrange, and a change to the list arranges it again; between those
    // two moments priority decides where a new tab is inserted and nothing else, so that a tab
    // the user has dragged stays where they put it. See Core/Ordering/AppPriority.cs.
    private bool _resortByPriority = true;

    // The group of each tab in _tabs, reused rather than rebuilt: it is read on every mouse
    // move while a tab is being dragged.
    private readonly List<string> _groupIds = new();

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

    // A drag that has moved a whole group keeps moving that group and nothing else, and waits
    // for the pointer to travel before moving it again. TabGrouping.PlanDrag and
    // TabStrip.MovedFarEnough say what each of those is for.
    private bool _draggingGroup;             // whether this drag has carried a group yet
    private int _lastGroupMoveX;             // pointer x when it last did
    private int _lastGroupMoveSlots;         // how far the row shifted when it last did

    /// <summary>How often the row scrolls while a tab is dragged against either end.</summary>
    private const int DragScrollIntervalMs = 120;

    private readonly ToolTip _toolTip = new();
    private string _toolTipText = string.Empty;

    // A live picture of the hovered window, when the setting asks for one. The tooltip answers
    // "what is this window called"; this answers "which of these identical ones is it".
    //
    // Created on the first preview rather than at startup, so a bar left on the default setting
    // never makes the window at all. PreviewWindow says why one is kept rather than one per tab.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Disposed in ReleaseResources, which Dispose and OnFormClosing call.")]
    private PreviewWindow _preview;

    // The window the preview is showing, or is about to show once the delay is up. Zero when
    // there is neither.
    private IntPtr _previewHwnd;

    private readonly System.Windows.Forms.Timer _previewTimer = new();

    /// <summary>
    /// How long the pointer rests on a tab before its window is drawn.
    /// </summary>
    /// <remarks>
    /// Longer than the tooltip's delay would make the preview feel slow, and shorter would put a
    /// picture on screen for every tab the pointer crosses on its way somewhere else. This is a
    /// little under the tooltip's 500 ms, so the two do not arrive together.
    /// </remarks>
    private const int PreviewDelayMs = 400;

    /// <summary>Widest and tallest a preview is drawn, in logical pixels.</summary>
    /// <remarks>
    /// A box rather than a width alone, so a tall window gives a tall preview rather than a
    /// strip up the side of the screen. What is drawn inside keeps the window's own shape.
    /// </remarks>
    private const int PreviewMaxLogical = 280;

    // The menu for a tab. It is not assigned to the ContextMenuStrip property, because which
    // menu to show depends on where the click landed, and that property would always show the
    // same one. The menu for the space around the tabs is the application's, and BarHost owns it.
    private ContextMenuStrip _tabMenu;

    // Menus replaced by a change of language, kept until the bar closes. See RebuildTabMenu.
    private readonly List<ContextMenuStrip> _retiredMenus = new();
    private IntPtr _menuTarget;              // the tab _tabMenu was opened on

    // The menu owns these three, and disposing it disposes them. CA2213 sees a disposable field
    // and asks for a second release here, which would be one more thing to keep in step.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by _tabMenu.Items, which releases it.")]
    private ToolStripItem _closeOthersItem;  // greyed out when there is nothing to act on

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by _tabMenu.Items, which releases it.")]
    private ToolStripItem _closeLeftItem;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by _tabMenu.Items, which releases it.")]
    private ToolStripItem _closeRightItem;

    // Greyed out for a window whose process could not be read: there is no application name to
    // put in the list, so the command would do nothing.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by _tabMenu.Items, which releases it.")]
    private ToolStripItem _excludeItem;

    private float _scale = 1.0f;
    private Font _font;

    // The interface text, in the language the settings ask for. Everything the user reads on the
    // bar and in its menus comes from here rather than from the line that draws it.
    private UiText _text = new UiText(Languages.English);

    // Colors, chosen to match the current Windows theme
    private Color _cBack, _cTab, _cTabActive, _cTabHover, _cText, _cTextActive, _cLine;
    private Color _cTabActiveOutline;

    // The accents a tab group can be marked with, in the shade of the palette in use. Held as
    // colours rather than as BarPalette's numbers so that painting a tab is a lookup.
    private readonly Color[] _accents = new Color[AppSettings.AccentCount];

    /// <summary>Which monitor this bar is on, as Windows names it, for example \\.\DISPLAY1.</summary>
    /// <remarks>
    /// The name rather than the rectangle, because the rectangle is what changes when a monitor
    /// is moved in the display settings. <see cref="BarHost"/> matches a bar to its monitor by
    /// this from one display change to the next.
    /// </remarks>
    internal string Device { get; }

    /// <summary>The monitor this bar sits on, in screen pixels.</summary>
    internal Rectangle Monitor { get; private set; }

    /// <summary>Whether the application is taking this bar away, rather than the user.</summary>
    private bool _hostClosing;

    internal MainForm(BarHost host, string device, Rectangle monitor)
    {
        _host = host;
        _settings = host.Settings;
        _processInfo = host.ProcessInfo;
        _text = host.Text;

        Device = device;
        Monitor = monitor;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        // The installer finds this window by its title, so that it can close a running bar
        // before it replaces or removes the executable. A change here is a change to
        // installer/WindowsSimpleTaskTabBar.iss as well.
        Text = "WindowsSimpleTaskTabBar";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        // Against the edge of its own monitor before the window exists, so Windows creates it
        // there and GetDpiForWindow answers with that monitor's scale factor rather than the
        // primary monitor's. OnHandleCreated settles the real position from the AppBar.
        Bounds = new Rectangle(monitor.Left, monitor.Bottom - _metrics.BarHeight,
                               monitor.Width, _metrics.BarHeight);

        ApplyTheme();

        _previewTimer.Interval = PreviewDelayMs;
        _previewTimer.Tick += (_, __) => OnPreviewDue();

        // Titles are drawn with an ellipsis, so the tooltip is the only way to read a
        // long one. ShowAlways is required because the bar is usually not the active window.
        _toolTip.ShowAlways = true;
        _toolTip.InitialDelay = 500;
        _toolTip.ReshowDelay = 200;
        _toolTip.AutoPopDelay = 10000;

        _tabMenu = BuildTabMenu();
    }

    // ---------------------------------------------------------------
    // The menu for a tab
    // ---------------------------------------------------------------

    /// <summary>
    /// Builds the menu for a single tab. It acts on <see cref="_menuTarget"/>, which is set
    /// from the tab under the pointer each time the menu is opened, so one menu serves them all.
    /// </summary>
    /// <remarks>
    /// Nothing here reports failure. A window belonging to an elevated process cannot be
    /// controlled from a normal one, and Windows gives no error for it either; a dialog saying
    /// so on every attempt would be worse than the silence.
    /// </remarks>
    private ContextMenuStrip BuildTabMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(_text[StringId.TabMenuClose], null,
            (_, __) => { WindowService.Close(_menuTarget); _host.MarkDirty(); });
        _closeOthersItem = menu.Items.Add(_text[StringId.TabMenuCloseOthers], null,
            (_, __) => CloseWindows(_menuTarget, Side.Both));
        _closeLeftItem = menu.Items.Add(_text[StringId.TabMenuCloseLeft], null,
            (_, __) => CloseWindows(_menuTarget, Side.Left));
        _closeRightItem = menu.Items.Add(_text[StringId.TabMenuCloseRight], null,
            (_, __) => CloseWindows(_menuTarget, Side.Right));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_text[StringId.TabMenuMinimize], null,
            (_, __) => { WindowService.Minimize(_menuTarget); _host.MarkDirty(); });

        menu.Items.Add(new ToolStripSeparator());
        _excludeItem = menu.Items.Add(_text[StringId.TabMenuExclude], null,
            (_, __) => _host.ExcludeApplication(_menuTarget));

        return menu;
    }

    /// <summary>Which side of a tab the closing commands act on.</summary>
    private enum Side { Left, Right, Both }

    /// <summary>
    /// Closes the windows beside one tab: those before it, those after it, or all of them.
    /// </summary>
    /// <remarks>
    /// The tab's position is looked up now rather than remembered from when the menu opened,
    /// because closing a window elsewhere in the meantime would have moved it along the row.
    /// </remarks>
    private void CloseWindows(IntPtr target, Side side)
    {
        int index = _tabs.FindIndex(t => t.Hwnd == target);
        if (index < 0) return;

        // Taken as a copy first: the refresh that follows rebuilds the list this walks.
        List<IntPtr> handles = _tabs.Select(t => t.Hwnd).ToList();

        for (int i = 0; i < handles.Count; i++)
        {
            bool beside = side switch
            {
                Side.Left => i < index,
                Side.Right => i > index,
                _ => i != index,
            };

            if (beside) WindowService.Close(handles[i]);
        }

        _host.MarkDirty();
    }

    /// <summary>
    /// Shows the menu for whatever is under the pointer: the tab, or the bar itself.
    /// </summary>
    private void ShowContextMenu(Point p)
    {
        int index = HitTest(p, out _);

        if (index >= 0)
        {
            _menuTarget = _tabs[index].Hwnd;

            // A command with nothing to close is greyed out rather than silently doing nothing.
            _closeOthersItem.Enabled = _tabs.Count > 1;
            _closeLeftItem.Enabled = index > 0;
            _closeRightItem.Enabled = index < _tabs.Count - 1;

            // The same rule: a window whose process could not be read has no application name
            // to exclude by, so the command is shown greyed rather than left to do nothing.
            _excludeItem.Enabled = _processInfo.Name(_menuTarget).Length > 0;

            _tabMenu.Show(this, p);
        }
        else
        {
            _host.ShowApplicationMenu(this, p);
        }
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

        ReadScale();
        RebuildMetrics();

        RegisterAppBar();
    }

    // ---------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------

    /// <summary>
    /// Recomputes every drawing size and rebuilds the font. Called whenever the bar height, the
    /// DPI or the language changes, so the sizes and the font can never disagree with them.
    /// </summary>
    private void RebuildMetrics()
    {
        _metrics = BarMetrics.For(AppSettings.HeightInPixels(_settings.BarHeight), _scale);

        _font?.Dispose();
        _font = UiFonts.Create(_text.FontFamily, _metrics.FontPixels, GraphicsUnit.Pixel);

        DiscardPreview();
    }

    /// <summary>
    /// Throws the preview window away, so the next one is built with the sizes and colours in
    /// use now.
    /// </summary>
    /// <remarks>
    /// Its border is a scaled size in a palette colour, and both are settled when the window is
    /// made. Building a new one is less to keep in step than reaching into the old one, and it
    /// happens only when the DPI, the bar height or the colours change. The pointer resting on a
    /// tab through one of those brings the preview back after the usual wait.
    /// </remarks>
    private void DiscardPreview()
    {
        _previewTimer.Stop();
        _previewHwnd = IntPtr.Zero;

        _preview?.Dispose();
        _preview = null;
    }

    /// <summary>
    /// Builds the tab menu again after the language has changed.
    /// </summary>
    /// <remarks>
    /// A <see cref="ToolStripItem"/> could have its text replaced instead, but that would mean a
    /// second list of which item holds which piece of text, beside the one in
    /// <see cref="BuildTabMenu"/>. The menu is built in one place and thrown away whole.
    ///
    /// The menu this replaces is kept rather than disposed. The language is changed in the
    /// settings dialog, which was opened from the Settings item of the application menu, and
    /// Windows Forms is still holding that menu further up the stack: disposing a menu from here
    /// fails once the dialog closes and the click finishes being handled. A menu is small and a
    /// language is changed rarely, so they are held until <see cref="ReleaseResources"/> runs.
    /// </remarks>
    private void RebuildTabMenu()
    {
        if (_tabMenu != null) _retiredMenus.Add(_tabMenu);
        _tabMenu = BuildTabMenu();
    }

    /// <summary>
    /// Takes a settings change that the application has already applied and written out, and
    /// brings this bar into line with it.
    /// </summary>
    /// <remarks>
    /// The row itself is left alone here. <see cref="BarHost"/> refreshes every bar once the
    /// last of them has been told, so grouping, exclusions and the priority order are applied
    /// on one pass rather than one per bar.
    /// </remarks>
    /// <param name="resortByPriority">
    /// Whether the priority list changed, which puts the whole row into it again. Every other
    /// setting leaves the row as it is, so a tab the user dragged is not pulled back because
    /// they went on to change the colours.
    /// </param>
    /// <param name="languageChanged">Whether the menu has to be built again.</param>
    internal void ApplySettingsChanged(bool resortByPriority, bool languageChanged)
    {
        if (resortByPriority) _resortByPriority = true;

        // Before the metrics, which build the font: the family comes from the language.
        _text = _host.Text;
        if (languageChanged) RebuildTabMenu();

        ApplyTheme();             // the colour setting may have changed
        RebuildMetrics();
        UpdateAppBarPosition();   // the reserved area changes, so other windows resize with it
    }

    /// <summary>Moves the bar onto its monitor's rectangle, after a display change.</summary>
    /// <remarks>
    /// The scale factor is read after the move rather than before it, because it is the scale
    /// factor of where the bar has landed that the sizes have to come from. Windows sends
    /// WM_DPICHANGED for a move it makes itself, but not for a monitor that was given a
    /// different scale factor while the bar was already sitting on it.
    /// </remarks>
    internal void MoveToMonitor(Rectangle monitor)
    {
        Monitor = monitor;
        UpdateAppBarPosition();

        if (!ReadScale()) return;

        RebuildMetrics();
        UpdateAppBarPosition();
    }

    /// <summary>
    /// Reads this monitor's scale factor, and says whether it is not the one in use.
    /// </summary>
    private bool ReadScale()
    {
        uint dpi = NativeMethods.GetDpiForWindow(Handle);
        if (dpi == 0) return false;

        float scale = dpi / 96f;
        if (Math.Abs(scale - _scale) < 0.001f) return false;

        _scale = scale;
        return true;
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

    /// <summary>
    /// Which edge of the screen the Windows taskbar is on, or the bottom when Windows does not
    /// answer.
    /// </summary>
    /// <remarks>
    /// The bottom rather than nothing, because that is where the taskbar is unless it has been
    /// moved, and it is where the bar sat before it could follow anything. A call that fails
    /// therefore leaves the bar where the user already expects it.
    /// </remarks>
    private static ScreenEdge TaskbarEdge()
    {
        var data = new NativeMethods.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        };

        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) == 0)
            return ScreenEdge.Bottom;

        if (data.uEdge == NativeMethods.ABE_TOP) return ScreenEdge.Top;
        if (data.uEdge == NativeMethods.ABE_LEFT) return ScreenEdge.Left;
        if (data.uEdge == NativeMethods.ABE_RIGHT) return ScreenEdge.Right;
        return ScreenEdge.Bottom;
    }

    /// <summary>
    /// Puts the bar against its own monitor's edge and reserves the strip it occupies there.
    /// </summary>
    /// <remarks>
    /// Windows takes one AppBar registration per monitor, so each bar asks for a strip of the
    /// monitor it is on and the windows maximized on that monitor stop above it.
    /// </remarks>
    private void UpdateAppBarPosition()
    {
        if (!_appBarRegistered) return;

        Rectangle screen = Monitor;
        int height = _metrics.BarHeight;

        // Settled on every call rather than once at start-up: this runs again whenever the
        // taskbar moves, so a taskbar dragged to the other edge takes the bar with it.
        ScreenEdge edge = BarPlacement.Resolve(_settings.BarEdge, TaskbarEdge());
        bool edgeChanged = edge != _edge;
        _edge = edge;

        BarBox wanted = BarPlacement.Requested(
            screen.Left, screen.Top, screen.Right, screen.Bottom, edge, height);

        var data = new NativeMethods.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = Handle,
            uEdge = edge == ScreenEdge.Top ? NativeMethods.ABE_TOP : NativeMethods.ABE_BOTTOM,
        };
        data.rc.left = wanted.Left;
        data.rc.top = wanted.Top;
        data.rc.right = wanted.Right;
        data.rc.bottom = wanted.Bottom;

        // Ask the system for free space at that edge; this keeps the bar clear of the taskbar.
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);

        BarBox placed = BarPlacement.Settled(
            new BarBox(data.rc.left, data.rc.top, data.rc.right, data.rc.bottom), edge, height);

        data.rc.left = placed.Left;
        data.rc.top = placed.Top;
        data.rc.right = placed.Right;
        data.rc.bottom = placed.Bottom;

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);

        NativeMethods.SetWindowPos(Handle, IntPtr.Zero,
            placed.Left, placed.Top, placed.Width, placed.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        if (!edgeChanged) return;

        // Moving to the other edge turns the bar over: the tabs hang from the other side and
        // their corners are rounded on it. The window is the same size either way, so nothing
        // else asks for this.
        DiscardPreview();
        LayoutTabs();
        Invalidate();
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
            if (ReadScale()) RebuildMetrics();
            UpdateAppBarPosition();

            // Which monitors exist may have changed with it, which is the application's to
            // settle: this bar's monitor may be the one that has gone.
            if (m.Msg == 0x007E && !_released) _host.OnDisplayChanged();
        }
        else if (m.Msg == 0x001A /* WM_SETTINGCHANGE */ && !_released && IsColourSetChange(m.LParam)
                 && _settings.Colours == ColourMode.FollowWindows)
        {
            // Windows switches the light and dark setting under the user, on a schedule for some
            // people, and the bar would otherwise keep the colours it read at start-up and sit
            // visibly wrong against the taskbar beside it.
            //
            // Only when the bar is set to follow Windows. A user who asked for the light palette
            // on a dark desktop chose that, and a scheduled switch must not undo it.
            ApplyTheme();
            Invalidate();
        }

        base.WndProc(ref m);
    }

    // ---------------------------------------------------------------
    // Refreshing the row
    // ---------------------------------------------------------------

    /// <summary>
    /// Takes the windows that are on this bar's monitor and rebuilds the row from them.
    /// </summary>
    /// <remarks>
    /// The windows are worked out once for the whole application, in
    /// <see cref="BarHost.Refresh"/>, and handed to each bar: enumerating them, reading the
    /// executable behind each one and fetching icons have the same answer on every monitor.
    /// What is left here is the part that is this bar's own - which order its row is in, how it
    /// is grouped and where it is scrolled to.
    ///
    /// A window that moved to another monitor simply stops appearing in this list and starts
    /// appearing in another bar's, on the pass after it moved.
    /// </remarks>
    /// <param name="current">The windows on this bar's monitor, after exclusions.</param>
    /// <param name="marked">
    /// The window the tabs mark as the one in front, which is on one bar at most.
    /// </param>
    internal void SetWindows(List<IntPtr> current, IntPtr marked)
    {
        if (_released) return;

        // Keep the existing order and append newly opened windows at the end.
        // Membership is tested through sets: this runs every 250 ms.
        var live = new HashSet<IntPtr>(current);
        _order.RemoveAll(h => !live.Contains(h));

        IntPtr foreground = marked;

        var known = new HashSet<IntPtr>(_order);
        List<string> priority = _settings.ApplicationPriority;
        bool prioritized = priority != null && priority.Count > 0;

        if (prioritized)
        {
            // The keys of the row, kept in step with _order as windows are inserted into it, so
            // that several windows opening at once are each placed against the row as it stands
            // rather than against the row as it was.
            List<string> keys = RowKeys();

            foreach (IntPtr h in current)
            {
                if (!known.Add(h)) continue;

                string key = _processInfo.Name(h);
                int at = AppPriority.InsertionIndex(keys, key, priority);

                _order.Insert(at, h);
                keys.Insert(at, key);
            }
        }
        else
        {
            // Appended, exactly as the bar did before this setting existed, and without reading
            // a single process.
            foreach (IntPtr h in current)
            {
                if (known.Add(h)) _order.Add(h);
            }
        }

        // Once at startup and once whenever the user changes the list, so that the setting shows
        // its effect straight away rather than only on the next window opened.
        if (_resortByPriority)
        {
            _resortByPriority = false;
            if (prioritized) SortByPriority(priority);
        }

        ArrangeByApplication();

        _tabs.Clear();
        foreach (IntPtr h in _order)
        {
            // Borrowed from the application's cache, which owns it. A tab never disposes the
            // icon it draws with: the same window can be handed to another bar tomorrow.
            _tabs.Add(new TabItem
            {
                Hwnd = h,
                Title = WindowService.GetTitle(h),
                Icon = _host.IconFor(h),
                Active = (h == foreground),
            });
        }

        MarkGroups();

        // A window closed mid-drag takes the drag with it: there is nothing left to move.
        if (_dragging && !_order.Contains(_dragHwnd)) EndDrag();

        LayoutTabs();

        // Not while dragging: the row must stay where the user is working, whatever gains focus.
        if (!_dragging) ScrollToForegroundTab(foreground);

        // The tab list has just been rebuilt, so a stored hover index would now point at
        // a different window. Take it from where the pointer actually is.
        RecomputeHover();
        UpdatePreview();
        UpdateToolTip();

        Invalidate();
    }

    /// <summary>
    /// Lets go of an icon the application is about to replace, so nothing here draws with one
    /// that has been disposed. The refresh that follows hands the new one back.
    /// </summary>
    internal void ForgetIcon(IntPtr hwnd)
    {
        foreach (TabItem tab in _tabs)
        {
            if (tab.Hwnd == hwnd) tab.Icon = null;
        }
    }

    // ---------------------------------------------------------------
    // Ordering the row by the user's priority list
    // ---------------------------------------------------------------

    /// <summary>
    /// The executable of each window in <see cref="_order"/>, in the order the row is drawn.
    /// </summary>
    /// <remarks>
    /// Answered from <see cref="ProcessInfoCache"/>, so this is a dictionary lookup per tab
    /// rather than a process query. It is only ever called with a priority list set: a bar
    /// nobody has ordered reads no process here at all.
    /// </remarks>
    private List<string> RowKeys()
    {
        var keys = new List<string>(_order.Count);
        foreach (IntPtr h in _order) keys.Add(_processInfo.Name(h));
        return keys;
    }

    /// <summary>
    /// Puts the whole row into the user's priority order, for the two moments that call for it:
    /// the bar starting, and the list being changed.
    /// </summary>
    /// <remarks>
    /// It is _order that is sorted, not _tabs, for the reason
    /// <see cref="ArrangeByApplication"/> gives: _order is the display order that survives the
    /// next refresh, and the two lists are moved together by index.
    ///
    /// Grouping runs after this, so one list settles both levels. Sorting brings the
    /// highest-ranked window to the front, and <c>TabGrouping.Arrange</c> puts a group where its
    /// first window sits, which carries that window's whole group to the front with it.
    /// </remarks>
    private void SortByPriority(List<string> priority)
    {
        if (_order.Count < 2) return;

        List<int> sorted = AppPriority.Sort(RowKeys(), priority);

        var handles = new List<IntPtr>(sorted.Count);
        foreach (int index in sorted) handles.Add(_order[index]);

        _order.Clear();
        _order.AddRange(handles);
    }

    // ---------------------------------------------------------------
    // Grouping the row by application
    // ---------------------------------------------------------------

    /// <summary>
    /// Brings the windows of one application together in <see cref="_order"/>.
    /// </summary>
    /// <remarks>
    /// It is _order that is arranged, not _tabs. _order is the display order that survives the
    /// next refresh, and dragging writes the same move to both lists by index; arranging one of
    /// them alone would let the two disagree.
    ///
    /// Nothing happens while grouping is off, down to not reading a single process, so the row
    /// behaves exactly as it did before this setting existed.
    /// </remarks>
    private void ArrangeByApplication()
    {
        _groupIds.Clear();
        if (!_settings.GroupByApplication || _order.Count == 0) return;

        foreach (IntPtr h in _order)
        {
            _groupIds.Add(TabGrouping.GroupIdFor(_processInfo.Name(h), _settings.Groups));
        }

        List<int> arranged = TabGrouping.Arrange(_groupIds);

        var handles = new List<IntPtr>(arranged.Count);
        var ids = new List<string>(arranged.Count);
        foreach (int index in arranged)
        {
            handles.Add(_order[index]);
            ids.Add(_groupIds[index]);
        }

        _order.Clear();
        _order.AddRange(handles);

        _groupIds.Clear();
        _groupIds.AddRange(ids);
    }

    /// <summary>
    /// Copies the group of each tab onto it, once the row has been rebuilt in arranged order.
    /// </summary>
    private void MarkGroups()
    {
        if (_groupIds.Count != _tabs.Count) return;

        bool[] marks = TabGrouping.Marks(_groupIds);
        int[] accents = TabGrouping.AccentsFor(_groupIds, _settings.Groups, AppSettings.AccentCount);

        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].GroupId = _groupIds[i];
            _tabs[i].Marked = marks[i];
            _tabs[i].Accent = accents[i];
        }
    }

    private void LayoutTabs()
    {
        int margin = _metrics.OuterMargin;

        // The gap goes between the tabs and the edge the desktop is on, so a bar at the top
        // hangs its tabs from its own top edge and leaves the gap underneath them. Either way a
        // tab stands on the edge the bar sits against.
        int top = AtTop ? 0 : _metrics.TopOffset;
        int height = ClientSize.Height - _metrics.TopOffset;
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
        UpdatePreview();
        UpdateToolTip();
        Invalidate();
    }

    // ---------------------------------------------------------------
    // Painting
    // ---------------------------------------------------------------
    /// <summary>
    /// Puts the palette the colour setting asks for into the fields the painting reads.
    /// </summary>
    /// <remarks>
    /// The Windows setting is read on every call, even when the user has chosen a fixed palette
    /// and the answer is thrown away. It is one registry value, read when the settings change or
    /// Windows repaints, so keeping the call in one place is worth more than the read costs.
    /// </remarks>
    private void ApplyTheme()
    {
        BarPalette palette = BarPalette.For(_settings.Colours, IsLightTheme());

        _cBack = FromRgb(palette.Background);
        _cTab = FromRgb(palette.Tab);
        _cTabHover = FromRgb(palette.TabHover);
        _cTabActive = FromRgb(palette.TabActive);
        _cTabActiveOutline = FromRgb(palette.TabActiveOutline);
        _cText = FromRgb(palette.Text);
        _cTextActive = FromRgb(palette.TextActive);
        _cLine = FromRgb(palette.Line);

        for (int i = 0; i < _accents.Length && i < palette.Accents.Count; i++)
            _accents[i] = FromRgb(palette.Accents[i]);

        BackColor = _cBack;

        // The preview's border is drawn in _cLine, which has just changed.
        DiscardPreview();
    }

    /// <summary>Turns one of Core's 0xRRGGBB numbers into an opaque colour.</summary>
    private static Color FromRgb(int rgb)
    {
        return Color.FromArgb(255, Color.FromArgb(rgb));
    }


    /// <summary>The colour a marked tab's accent is drawn in.</summary>
    private Color GroupAccent(TabItem tab)
    {
        int accent = tab.Accent;
        if (accent < 0 || accent >= _accents.Length) return _cLine;

        return _accents[accent];
    }

    /// <summary>
    /// True when a WM_SETTINGCHANGE names the section Windows rewrites when the light and dark
    /// setting changes.
    /// </summary>
    /// <remarks>
    /// Windows sends WM_SETTINGCHANGE for many unrelated reasons, so the section name is the
    /// filter: without it the bar would repaint on changes that have nothing to do with colour.
    /// lParam points at the name for some of those messages and is null for others, which is not
    /// an error.
    /// </remarks>
    private static bool IsColourSetChange(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return false;
        return Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet";
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
        // A paint request can still arrive between the close and the window being destroyed,
        // and by then the font and the icons this method draws with are gone.
        if (_released) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(_cBack);

        // The line marks the bar off from the desktop, so it is drawn on the side the desktop
        // is on: the bar's top edge at the bottom of the screen, its bottom edge at the top.
        int line = AtTop ? ClientSize.Height - 1 : 0;
        using (var pen = new Pen(_cLine))
            g.DrawLine(pen, 0, line, ClientSize.Width, line);

        if (_tabs.Count == 0)
        {
            // Centred in the bar rather than placed at a fixed offset, so it stays put when
            // the bar height changes.
            var emptyRect = new Rectangle(
                _metrics.Padding, AtTop ? 0 : _metrics.TopOffset,
                ClientSize.Width - _metrics.Padding * 2, ClientSize.Height - _metrics.TopOffset);
            TextRenderer.DrawText(g, _text[StringId.BarNoWindows], _font, emptyRect, _cText,
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

        // After the tabs, so a band drawn across a gap sits on the bar rather than under the
        // neighbour it joins.
        DrawGroupSeparators(g);

        // The dragged tab goes last, so it passes over its neighbours rather than under them.
        if (dragIndex >= 0 && IsTabVisible(_tabs[dragIndex]))
            DrawTab(g, _tabs[dragIndex], dragIndex);

        g.ResetClip();
        DrawScrollButtons(g);
    }

    private void DrawTab(Graphics g, TabItem tab, int index)
    {
        Color fill = tab.Active ? _cTabActive : (index == _hoverIndex ? _cTabHover : _cTab);

        // Every tab is the same size, the active one included. It is marked by an outline and
        // nothing else: drawing it taller moved the group accent off the top edge it shares with
        // the tabs beside it, and left the two out of line.
        //
        // The active tab's foot alone goes past the edge of the bar, by the width of the
        // outline. The outline follows a closed path, and an edge left on the last row of
        // pixels would be drawn as a line under the tab; pushed out of the client area it is not
        // drawn at all, which is what a tab standing on the edge of the bar should look like.
        // The fill does not care: everything past the bar is clipped away either way. The foot
        // is the tab's bottom on a bar at the bottom of the screen and its top on one at the
        // top, which is the edge it stands on in each case.
        Rectangle body = tab.Active
            ? (AtTop
                ? Rectangle.FromLTRB(tab.Bounds.Left, tab.Bounds.Top - _metrics.ActiveOutlineWidth,
                                     tab.Bounds.Right, tab.Bounds.Bottom)
                : Rectangle.FromLTRB(tab.Bounds.Left, tab.Bounds.Top,
                                     tab.Bounds.Right, tab.Bounds.Bottom + _metrics.ActiveOutlineWidth))
            : tab.Bounds;

        using (GraphicsPath path = Rounded(body, _metrics.CornerRadius, roundTop: !AtTop))
        {
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            if (tab.Active)
            {
                using var pen = new Pen(_cTabActiveOutline, _metrics.ActiveOutlineWidth);
                g.DrawPath(pen, path);
            }

            // After the outline, so a marked tab keeps the full thickness of its accent and the
            // group still reads as one band along the free edge of the row. The outline is left
            // down the sides, which is where it does the marking.
            if (tab.Marked) DrawGroupBand(g, path, tab);
        }

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
    /// The accent along a grouped tab's free edge, clipped to the tab's own outline so it
    /// follows the rounded corners rather than squaring them off.
    /// </summary>
    /// <remarks>
    /// Save and Restore rather than ResetClip: OnPaint has clipped the row to _contentRect, and
    /// resetting would drop that as well and let a tab draw over the scroll arrows.
    ///
    /// SetClip takes the path itself. Reading the Clip property, or intersecting a Region built
    /// from the path, would each hand back a Region this method then has to release.
    /// </remarks>
    private void DrawGroupBand(Graphics g, GraphicsPath path, TabItem tab)
    {
        GraphicsState state = g.Save();
        g.SetClip(path, CombineMode.Intersect);

        using (var brush = new SolidBrush(GroupAccent(tab)))
            g.FillRectangle(brush, tab.Bounds.Left, BandTop(tab.Bounds),
                            tab.Bounds.Width, _metrics.GroupBandHeight);

        g.Restore(state);
    }

    /// <summary>
    /// Carries a group's accent across the gap between two of its tabs, so the group reads as one
    /// band, and draws a rule where one group ends and the next begins.
    /// </summary>
    /// <remarks>
    /// Nothing here changes where a tab sits. The gap is the one the layout already leaves
    /// between every pair of tabs, so the position a tab is drawn at and the position it would be
    /// dropped at still agree, and TabStrip keeps its one width for every tab.
    ///
    /// Not while a tab is being dragged: the dragged tab is drawn away from its slot, so a band
    /// joining it to its neighbours would point at the wrong place.
    /// </remarks>
    private void DrawGroupSeparators(Graphics g)
    {
        if (!_settings.GroupByApplication || _dragging) return;

        for (int i = 0; i < _tabs.Count - 1; i++)
        {
            TabItem left = _tabs[i];
            TabItem right = _tabs[i + 1];

            if (!IsTabVisible(left) && !IsTabVisible(right)) continue;

            int x = left.Bounds.Right;
            int width = right.Bounds.Left - x;
            if (width <= 0) continue;

            bool sameGroup = left.Marked && right.Marked
                             && string.Equals(left.GroupId, right.GroupId,
                                              StringComparison.OrdinalIgnoreCase);

            if (sameGroup)
            {
                using var brush = new SolidBrush(GroupAccent(left));
                g.FillRectangle(brush, x, BandTop(left.Bounds), width, _metrics.GroupBandHeight);
            }
            else if (left.Marked || right.Marked)
            {
                using var pen = new Pen(_cLine, _metrics.GroupDividerWidth);
                int centre = x + width / 2;
                g.DrawLine(pen, centre, left.Bounds.Top, centre, left.Bounds.Bottom);
            }
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

    /// <summary>
    /// Where a group's accent band sits inside a tab: along the tab's free edge, which is the
    /// one the desktop is on.
    /// </summary>
    /// <remarks>
    /// The same edge the corners are rounded on, so the band follows the rounding rather than
    /// cutting across the square foot the tab stands on.
    /// </remarks>
    private int BandTop(Rectangle tab)
    {
        return AtTop ? tab.Bottom - _metrics.GroupBandHeight : tab.Top;
    }

    /// <summary>
    /// A tab's outline: rounded on the edge facing the desktop, square on the edge it stands on.
    /// </summary>
    /// <param name="roundTop">
    /// Whether the top corners are the rounded pair, which they are while the bar is at the
    /// bottom of the screen. A bar at the top hangs its tabs the other way up.
    /// </param>
    private static GraphicsPath Rounded(Rectangle r, int radius, bool roundTop)
    {
        var path = new GraphicsPath();
        int d = radius * 2;

        if (roundTop)
        {
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        }
        else
        {
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.AddLine(r.Left, r.Top, r.Right, r.Top);
        }

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

        // The pointer may be reading the preview, which is off the bar. The tab it belongs to
        // stays hovered until the pointer leaves the preview as well - found by window rather
        // than by index, because the row may have been rebuilt while the pointer sat there. A
        // window that closed meanwhile answers -1, and the preview goes with it.
        if (PointerIsAtPreview)
        {
            _hoverIndex = _tabs.FindIndex(t => t.Hwnd == _previewHwnd);
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

        // The preview first: it is what the tooltip stands down for, so the tooltip has to be
        // asked after the old preview has gone rather than before.
        UpdatePreview();
        UpdateToolTip();
        Invalidate();
    }

    /// <summary>
    /// Shows the full window title of the hovered tab. Titles are drawn with an ellipsis,
    /// so this is the only way to read one that does not fit.
    /// </summary>
    private void UpdateToolTip()
    {
        // Nothing while a preview is on screen: it draws the same title under its picture, and
        // two answers arriving at once are harder to read than either alone.
        string text = _hoverIndex >= 0 && _hoverIndex < _tabs.Count && !PreviewIsShowing
            ? _tabs[_hoverIndex].Title
            : string.Empty;

        // Setting the same text again restarts the tooltip and makes it flicker.
        if (text == _toolTipText) return;

        _toolTipText = text;
        _toolTip.SetToolTip(this, text);
    }

    /// <summary>
    /// Brings the preview into line with the tab under the pointer: starts the wait for a new
    /// one, and takes down the one showing as soon as the pointer is somewhere else.
    /// </summary>
    /// <remarks>
    /// Called from everywhere the hovered tab can change, and from the settings, so that turning
    /// the preview off closes the one on screen rather than leaving it until the pointer moves.
    /// </remarks>
    private bool PreviewIsShowing => _preview != null && _preview.Visible;

    private void UpdatePreview()
    {
        IntPtr wanted = PreviewTarget();
        if (wanted == _previewHwnd) return;

        _previewHwnd = wanted;
        _previewTimer.Stop();
        _preview?.HidePreview();

        if (wanted != IntPtr.Zero) _previewTimer.Start();
    }


    /// <summary>
    /// The window a preview should be showing, or zero for none.
    /// </summary>
    /// <remarks>
    /// A minimized window is not left out, and needs no special case: Windows keeps enough of one
    /// that the compositor draws it like any other. The panel draws the window's icon behind the
    /// picture regardless, which covers whatever it has no picture of.
    /// </remarks>
    private IntPtr PreviewTarget()
    {
        if (_released || !_settings.ShowWindowPreview || _dragging) return IntPtr.Zero;
        if (_hoverIndex < 0 || _hoverIndex >= _tabs.Count) return IntPtr.Zero;

        TabItem tab = _tabs[_hoverIndex];
        return IsTabVisible(tab) ? tab.Hwnd : IntPtr.Zero;
    }

    /// <summary>
    /// Whether the pointer is on the preview, or on the way between it and the tab below.
    /// </summary>
    /// <remarks>
    /// The preview is a window of its own, so the pointer moving onto it leaves the bar. Without
    /// this the bar would clear its hover as the pointer arrived, and the preview would take
    /// itself down at the moment the user reached for it.
    ///
    /// The way between them is the strip the bar leaves beside the tabs, <see cref="BarMetrics.
    /// TopOffset"/> tall, which belongs to no tab: the preview covers the first row of it and the
    /// tabs start past it, so a couple of pixels in between are on the bar and on nothing. A
    /// pointer moving fast crosses them in one message and a slow one lands in them, which is
    /// what made this show up as "it disappears if I move slowly". On a bar at the top of the
    /// screen the strip is at the foot of the bar, because that is the side the preview is on.
    /// </remarks>
    private bool PointerIsAtPreview
    {
        get
        {
            if (_preview == null || !_preview.Visible) return false;
            if (_preview.Bounds.Contains(MousePosition)) return true;

            Point p = PointToClient(MousePosition);
            if (!ClientRectangle.Contains(p)) return false;

            return AtTop
                ? p.Y >= ClientSize.Height - _metrics.TopOffset
                : p.Y < _metrics.TopOffset;
        }
    }

    /// <summary>
    /// The pointer has left the preview. It goes unless the bar has it back, in which case the
    /// bar's own mouse events settle where the hover is.
    /// </summary>
    private void OnPreviewPointerLeft()
    {
        if (_released) return;
        if (ClientRectangle.Contains(PointToClient(MousePosition))) return;

        SetHover(-1, false);
    }

    /// <summary>Shows the preview, once the pointer has rested long enough for it.</summary>
    private void OnPreviewDue()
    {
        _previewTimer.Stop();

        // The pointer may have moved on, or the window closed, while the delay ran.
        IntPtr hwnd = _previewHwnd;
        if (hwnd == IntPtr.Zero || PreviewTarget() != hwnd) return;

        ShowPreview(hwnd);
    }

    private void ShowPreview(IntPtr hwnd)
    {
        int index = _tabs.FindIndex(t => t.Hwnd == hwnd);
        if (index < 0) return;

        int border = Scaled(1);

        if (_preview == null)
        {
            _preview = new PreviewWindow(border, _cBack, _cLine, _cText,
                                         _text.FontFamily, _metrics.FontPixels);
            _preview.PointerLeft += (_, __) => OnPreviewPointerLeft();
        }

        if (!_preview.Register(hwnd, out int sourceWidth, out int sourceHeight))
        {
            _preview.HidePreview();
            return;
        }

        Rectangle tab = RectangleToScreen(_tabs[index].Bounds);
        Rectangle screen = Monitor;

        // No gap. The panel sits against the bar's free edge so the pointer can travel from the
        // tab onto it without crossing anything in between: a gap is desktop, and the moment the
        // pointer touched it the bar would lose its hover and take the preview down. The
        // taskbar's own thumbnails sit against it for the same reason.
        const int gap = 0;

        // The desktop is on the other side of the bar from the edge it sits on, so a bar at the
        // top of the screen hangs its previews underneath itself.
        bool below = AtTop;
        int barEdge = below ? Bottom : Top;

        // As large as the box allows, and never taller than the room between the bar and the far
        // edge of the screen, which the title and the frame are taken out of first. Fit is what
        // keeps the whole panel inside that room, so Place has nothing to bring back onto the
        // screen.
        int max = Scaled(PreviewMaxLogical);
        int room = (below ? screen.Bottom - Bottom : Top - screen.Top)
                   - border * 2 - _preview.TitleHeight;
        int maxHeight = max < room ? max : room;

        PreviewPlacement.Fit(sourceWidth, sourceHeight, max, maxHeight,
                             out int width, out int height);

        if (width <= 0 || height <= 0)
        {
            _preview.HidePreview();
            return;
        }

        // The title travels with the picture, so it is part of what is being placed.
        PreviewBox content = PreviewPlacement.Place(width, height + _preview.TitleHeight,
            tab.Left, tab.Width, barEdge, screen.Left, screen.Right, gap, below);

        _preview.Present(content, _tabs[index].Title, _tabs[index].Icon);

        // The preview now says the title itself, so the tooltip stops saying it too.
        UpdateToolTip();
    }

    /// <summary>A logical size in device pixels, for the sizes the preview is built from.</summary>
    private int Scaled(int logical)
    {
        int value = (int)Math.Round(logical * (double)_scale);
        return value < 1 ? 1 : value;
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

        // On nothing, between the tabs and the preview above them. The hover is left where it
        // was so the preview survives the crossing; PointerIsAtPreview says why.
        if (index < 0 && PointerIsAtPreview) return;

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

        // Onto the preview rather than away from the bar: the preview stays, and takes itself
        // down when the pointer leaves it.
        if (PointerIsAtPreview) return;

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
            _host.MarkDirty();
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

        if (e.Button == MouseButtons.Right)
        {
            // Ignored while the left button is still doing something: a menu over a tab that is
            // being dragged would be acting on a moving target.
            if (!_dragging && _pressedHwnd == IntPtr.Zero) ShowContextMenu(e.Location);
            return;
        }

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

        _host.MarkDirty();
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
        _draggingGroup = false;
        _dragStartIndex = _order.IndexOf(_dragHwnd);
        _orderBeforeDrag = new List<IntPtr>(_order);
        _lastDragScroll = Environment.TickCount;

        SetHover(-1, false);
        UpdatePreview();   // SetHover says nothing when the hover was already clear
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

        if (_settings.GroupByApplication && _groupIds.Count == _tabs.Count)
        {
            // Inside its own group the tab moves alone; once the pointer passes beyond it, the
            // whole group travels as a block. See TabGrouping.PlanDrag for why one is allowed
            // and the other is not.
            TabGrouping.DragMove move =
                TabGrouping.PlanDrag(target, index, _groupIds, _draggingGroup);

            // Anything that crosses a group waits for the pointer to travel as far as the row
            // shifted last time. Without it the row flickers, and a tab that has just jumped a
            // group jumps straight back on the next mouse move. A move inside a group is not
            // held back: it already has the room it needs.
            bool held = move.CrossesGroups && _draggingGroup
                        && !TabStrip.MovedFarEnough(_dragX, _lastGroupMoveX, _strip.TabWidth,
                                                    _lastGroupMoveSlots);

            if (!held && !move.IsNothing)
            {
                // _tabs is what is drawn now, _order is what survives the next refresh, and
                // _groupIds is what the next drag step reads. A block move reorders the groups,
                // so all three have to move together.
                TabStrip.MoveRange(_order, move.Start, move.Count, move.To);
                TabStrip.MoveRange(_tabs, move.Start, move.Count, move.To);
                TabStrip.MoveRange(_groupIds, move.Start, move.Count, move.To);

                if (move.CrossesGroups)
                {
                    _draggingGroup = true;
                    _lastGroupMoveX = _dragX;
                    _lastGroupMoveSlots = move.Distance;
                }
            }
        }
        else if (target != index)
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
        UpdatePreview();
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

        // Through the application, which hands every bar its windows again. The order this has
        // just put back is kept: a refresh adds and removes windows rather than reordering the
        // row.
        _host.RefreshNow();
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

    /// <summary>
    /// Takes this bar away because the application asked: its monitor was unplugged, the
    /// setting no longer wants a bar there, or the application is closing.
    /// </summary>
    /// <remarks>
    /// The flag is what tells <see cref="OnFormClosing"/> that the user did not close this bar,
    /// so the application is not ended with it.
    /// </remarks>
    internal void CloseForHost()
    {
        _hostClosing = true;
        Close();
        Dispose();
    }

    /// <summary>
    /// Releases everything this bar owns: the font, the tooltip, the preview, the tab menu and
    /// the AppBar registration.
    /// </summary>
    /// <remarks>
    /// Called from two places, and <see cref="_released"/> makes the second call do nothing.
    /// <see cref="OnFormClosing"/> calls it so the desktop gets its space back as soon as the
    /// bar is closed, and <see cref="Dispose(bool)"/> calls it so nothing is left registered
    /// when the form is disposed without having been closed.
    ///
    /// The icons and the process cache are not released here. They belong to
    /// <see cref="BarHost"/> and are shared with the other bars, which are still drawing with
    /// them when one bar goes because its monitor was unplugged.
    /// </remarks>
    private void ReleaseResources()
    {
        if (_released) return;
        _released = true;

        _previewTimer.Stop();
        _previewTimer.Dispose();
        _preview?.Dispose();
        _preview = null;
        _previewHwnd = IntPtr.Zero;

        // The icons are the application's, so the tabs only let go of them.
        _tabs.Clear();
        _groupIds.Clear();
        _order.Clear();

        _toolTip.Dispose();

        _font?.Dispose();
        _font = null;

        _tabMenu?.Dispose();

        foreach (ContextMenuStrip menu in _retiredMenus)
            menu.Dispose();
        _retiredMenus.Clear();

        UnregisterAppBar();
    }

    /// <summary>
    /// Releases the resources once the close is settled, and ends the application when it was
    /// the user who closed the bar.
    /// </summary>
    /// <remarks>
    /// The base call comes first because a handler of the <c>FormClosing</c> event may cancel
    /// the close, and a bar that goes on running still needs its font and its tooltip.
    ///
    /// Closing one bar closes the application, which is what the Exit entry in the menu does
    /// and what Windows does when it ends the session. A bar the application itself is taking
    /// away - an unplugged monitor, or the setting - comes through
    /// <see cref="CloseForHost"/> instead and leaves the rest of them alone.
    /// </remarks>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        ReleaseResources();
        if (!_hostClosing) _host.BarClosed(this);
    }

    /// <summary>
    /// Releases the resources before the base implementation destroys the window handle.
    /// Removing the AppBar registration needs a handle that still exists.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseResources();
        base.Dispose(disposing);
    }
}
