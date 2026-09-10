using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WindowsSimpleTaskTabBar.Core.Filtering;
using WindowsSimpleTaskTabBar.Core.Focus;
using WindowsSimpleTaskTabBar.Core.Grouping;
using WindowsSimpleTaskTabBar.Core.Layout;
using WindowsSimpleTaskTabBar.Core.Localization;
using WindowsSimpleTaskTabBar.Core.Preview;
using WindowsSimpleTaskTabBar.Core.Settings;
using WindowsSimpleTaskTabBar.Core.Theme;
using WindowsSimpleTaskTabBar.Core.Update;
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

        // Which group the tab belongs to, when grouping is on. GroupId is empty when it is off
        // and when the owning executable could not be read; Marked is false for a group of one,
        // where an accent would have nothing to say.
        public string GroupId = string.Empty;
        public int Accent = -1;
        public bool Marked;
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
    private bool _released;               // see ReleaseResources

    private readonly List<TabItem> _tabs = new();
    private readonly List<IntPtr> _order = new();          // keeps the display order stable
    private readonly Dictionary<IntPtr, CachedIcon> _iconCache = new();
    private readonly ProcessInfoCache _processInfo = new();

    // Every window that could be a tab, including the ones the user excludes. _order holds
    // what is shown; this holds what was offered, so the settings dialog can still list an
    // application whose windows it has just taken off the bar, and so the process cache keeps
    // its entry for a window that is looked at on every refresh but never drawn.
    private readonly List<IntPtr> _candidates = new();

    // The group of each tab in _tabs, reused rather than rebuilt: it is read on every mouse
    // move while a tab is being dragged.
    private readonly List<string> _groupIds = new();

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

    // The window the tabs mark as the one in front. Not always the foreground window: see
    // WindowToMark, and Core/Focus/ActiveMark.cs for the rule itself.
    private IntPtr _markedWindow;

    // Read once. A process cannot change the id it was given.
    private static readonly uint OwnProcessId = NativeMethods.GetCurrentProcessId();

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

    // Two menus for the bar: one for a tab, one for the space around the tabs. Neither is
    // assigned to the ContextMenuStrip property, because which one to show depends on where
    // the click landed, and that property would always show the same one.
    private ContextMenuStrip _appMenu;
    private ContextMenuStrip _tabMenu;

    // Menus replaced by a change of language, kept until the bar closes. See RebuildMenus.
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

    private NotifyIcon _trayIcon;

    // The update check. _updateAvailableTag names a release newer than this build, and is what
    // both menus read when they open. It is seeded from the settings file at startup and set
    // again by each check; it is empty when nothing newer is known.
    private bool _updateCheckStarted;
    private bool _updateCheckRunning;
    private string _updateAvailableTag = string.Empty;

    // Only a record of which dialog is open, so a second request can bring it forward. The
    // using statement in ShowSettings owns it, and this field is null again by the time that
    // statement ends.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the using statement in ShowSettings.")]
    private SettingsForm _settingsForm;
    private IntPtr _trayIconHandle;   // owned by this class; see LoadSmallApplicationIcon

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
        _text = TextForSettings();
        _updateAvailableTag = KnownNewerRelease();

        ApplyTheme();

        _timer.Interval = 250;
        _timer.Tick += (_, __) => OnTimerTick();

        _previewTimer.Interval = PreviewDelayMs;
        _previewTimer.Tick += (_, __) => OnPreviewDue();

        // Titles are drawn with an ellipsis, so the tooltip is the only way to read a
        // long one. ShowAlways is required because the bar is usually not the active window.
        _toolTip.ShowAlways = true;
        _toolTip.InitialDelay = 500;
        _toolTip.ReshowDelay = 200;
        _toolTip.AutoPopDelay = 10000;

        _appMenu = BuildMenu();
        _tabMenu = BuildTabMenu();
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
        menu.Items.Add(_text[StringId.MenuSettings], null, (_, __) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UpdateMenuText, null, (_, __) => OnUpdateMenuClicked()).Name = UpdateItemName;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_text[StringId.MenuRefresh], null, (_, __) => { _dirty = true; RefreshTabs(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_text[StringId.MenuExit], null, (_, __) => Close());

        // What the update entry says depends on state that changes while the menu is closed, and
        // there are two menus built from here. Setting the text as each one opens keeps them in
        // step without a field pointing into either of them.
        menu.Opening += (_, __) =>
        {
            ToolStripItem item = menu.Items[UpdateItemName];
            if (item != null) item.Text = UpdateMenuText;
        };

        return menu;
    }

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
            (_, __) => { WindowService.Close(_menuTarget); _dirty = true; });
        _closeOthersItem = menu.Items.Add(_text[StringId.TabMenuCloseOthers], null,
            (_, __) => CloseWindows(_menuTarget, Side.Both));
        _closeLeftItem = menu.Items.Add(_text[StringId.TabMenuCloseLeft], null,
            (_, __) => CloseWindows(_menuTarget, Side.Left));
        _closeRightItem = menu.Items.Add(_text[StringId.TabMenuCloseRight], null,
            (_, __) => CloseWindows(_menuTarget, Side.Right));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_text[StringId.TabMenuMinimize], null,
            (_, __) => { WindowService.Minimize(_menuTarget); _dirty = true; });

        menu.Items.Add(new ToolStripSeparator());
        _excludeItem = menu.Items.Add(_text[StringId.TabMenuExclude], null,
            (_, __) => ExcludeApplication(_menuTarget));

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

        _dirty = true;
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
            _appMenu.Show(this, p);
        }
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

        _trayIcon.BalloonTipClicked += (_, __) => OpenReleasePage();
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

    /// <summary>The interface text in the language the settings ask for.</summary>
    /// <remarks>
    /// The setting is normally empty, which means the language Windows is set to. Anything the
    /// application has no table for ends at English; <c>Languages.Resolve</c> holds the rules.
    /// </remarks>
    private UiText TextForSettings()
    {
        return new UiText(
            Languages.Resolve(_settings.Language, CultureInfo.CurrentUICulture.Name));
    }

    /// <summary>
    /// Builds both menus again, and the tray icon's, after the language has changed.
    /// </summary>
    /// <remarks>
    /// A <see cref="ToolStripItem"/> could have its text replaced instead, but that would mean a
    /// second list of which item holds which piece of text, beside the one in
    /// <see cref="BuildMenu"/>. The menus are built in one place and thrown away whole.
    ///
    /// The menus this replaces are kept rather than disposed. The language is changed in the
    /// settings dialog, which was opened from the Settings item of one of these menus, and
    /// Windows Forms is still holding that menu further up the stack: disposing it here fails
    /// once the dialog closes and the click finishes being handled. A menu is small and a
    /// language is changed rarely, so they are held until <see cref="ReleaseResources"/> runs.
    /// </remarks>
    private void RebuildMenus()
    {
        Retire(_appMenu);
        Retire(_tabMenu);

        _appMenu = BuildMenu();
        _tabMenu = BuildTabMenu();

        if (_trayIcon != null)
        {
            // The NotifyIcon does not own its menu, so the one it held is retired here too.
            Retire(_trayIcon.ContextMenuStrip);
            _trayIcon.ContextMenuStrip = BuildMenu();
        }
    }

    /// <summary>Keeps a menu that is no longer shown, to be disposed when the bar closes.</summary>
    private void Retire(ContextMenuStrip menu)
    {
        if (menu != null) _retiredMenus.Add(menu);
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

        using var form = new SettingsForm(_settings, ApplySettings, RunningApplications, () => _text);
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
        // In place, so the bar reads the same values the file will hold. A name the settings
        // dialog could not keep, or an application claimed by two groups, is settled here rather
        // than only on the way out.
        _settings.Normalize();

        // Before the metrics, which build the font: the family comes from the language.
        UiText text = TextForSettings();
        if (text.Language != _text.Language)
        {
            _text = text;
            RebuildMenus();
        }

        ApplyTheme();             // the colour setting may have changed
        RebuildMetrics();
        UpdateAppBarPosition();   // the reserved area changes, so other windows resize with it

        // Grouping is applied while the row is rebuilt, so a change to it shows on the next
        // refresh rather than on this call. The dialog says changes apply straight away, so the
        // refresh is asked for here instead of waiting up to two seconds for the safety net.
        RefreshTabs();

        // After the refresh, which recomputes the hover the preview follows. Turning the setting
        // off is what this is here for: the preview on screen goes now rather than when the
        // pointer next moves.
        UpdatePreview();
        UpdateToolTip();

        SettingsStore.Save(_settings);
    }

    // ---------------------------------------------------------------
    // Telling the user a new version exists
    // ---------------------------------------------------------------

    /// <summary>The name the update entry is found by in both menus.</summary>
    private const string UpdateItemName = "update";

    /// <summary>
    /// How long after the bar appears the automatic check runs, in timer ticks of 250 ms.
    /// </summary>
    /// <remarks>
    /// Ten seconds. The bar is usually started at logon, where the network is often not up yet
    /// when the first window is drawn; checking immediately would fail for a reason that has
    /// nothing to do with whether a release exists. Waiting also keeps the request off the path
    /// that puts the bar on screen.
    /// </remarks>
    private const int UpdateCheckDelayTicks = 40;

    /// <summary>
    /// The release the user was last told about, when it is still newer than this build.
    /// </summary>
    /// <remarks>
    /// The check runs at most once a day, so a bar started again the same day runs none, and
    /// without this the menu would fall back to "Check for updates..." while a newer release was
    /// sitting in the settings file. The notice itself is still shown once per release; this is
    /// only what the menu says.
    ///
    /// The comparison is against the running build rather than a plain "is it set" test, because
    /// the user updates by replacing the executable. The settings file still names the release
    /// they were told about, and after they act on it that release is the one they are running.
    /// </remarks>
    private string KnownNewerRelease()
    {
        string told = _settings.LastNoticedRelease;
        return ReleaseVersion.IsNewer(told, UpdateService.RunningVersion) ? told : string.Empty;
    }

    /// <summary>One entry serves both jobs, so a five-item menu does not become seven.</summary>
    private string UpdateMenuText =>
        _updateAvailableTag.Length > 0
            ? _text.Format(StringId.MenuUpdateAvailable, _updateAvailableTag)
            : _text[StringId.MenuCheckForUpdates];

    /// <summary>
    /// Starts the automatic check, once per run of the application.
    /// </summary>
    /// <remarks>
    /// <see cref="_updateCheckStarted"/> is set whatever the answer, including when the check is
    /// turned off or is not due, so this costs one comparison per tick after the first.
    /// </remarks>
    private void StartUpdateCheckOnce()
    {
        if (_updateCheckStarted || _tickCount < UpdateCheckDelayTicks) return;
        _updateCheckStarted = true;

        // Null means the setting was never chosen, which is on. See AppSettings.
        if (_settings.CheckForUpdates == false) return;
        if (!UpdateCheckSchedule.IsDue(_settings.LastUpdateCheckUtc, DateTime.UtcNow)) return;

        RunUpdateCheck(report: false);
    }

    /// <summary>
    /// Runs a check on a pool thread and brings the answer back to this thread.
    /// </summary>
    /// <param name="report">
    /// True when the user asked for the check from the menu, which is always answered. The
    /// automatic check passes false and says nothing unless there is something newer.
    /// </param>
    private void RunUpdateCheck(bool report)
    {
        if (_updateCheckRunning) return;
        _updateCheckRunning = true;

        // A pool thread is a background thread, so a request still in flight cannot hold up Exit.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            UpdateCheckResult result = UpdateService.Check();

            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() => OnUpdateChecked(result, report)));
            }
            catch (ObjectDisposedException)
            {
                // The bar was closed between the test above and the post. Nothing is left to
                // tell, and _updateCheckRunning goes with the form.
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    /// <summary>
    /// Acts on the answer, on the user interface thread.
    /// </summary>
    private void OnUpdateChecked(UpdateCheckResult result, bool report)
    {
        _updateCheckRunning = false;
        if (_released) return;

        if (result.Outcome == UpdateCheckOutcome.Failed)
        {
            // The time is not recorded: a machine that was offline at logon should try again on
            // the next start rather than wait another day.
            if (report)
            {
                MessageBox.Show(this, _text[StringId.UpdateCheckFailed],
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        _settings.LastUpdateCheckUtc = UpdateCheckSchedule.Stamp(DateTime.UtcNow);

        if (result.Outcome == UpdateCheckOutcome.UpdateAvailable)
        {
            _updateAvailableTag = result.LatestTag;

            bool alreadyTold =
                ReleaseVersion.IsSameRelease(result.LatestTag, _settings.LastNoticedRelease);
            _settings.LastNoticedRelease = result.LatestTag;

            if (report)
            {
                if (MessageBox.Show(this,
                        _text.Format(StringId.UpdateAvailableAsk, result.LatestTag),
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                    == DialogResult.Yes)
                {
                    OpenReleasePage();
                }
            }
            else if (!alreadyTold)
            {
                ShowUpdateNotice(result.LatestTag);
            }
        }
        else
        {
            // GitHub says there is nothing newer, so anything seeded from the settings file at
            // startup is out of date. Leaving it would keep offering an update to a release this
            // build already is.
            _updateAvailableTag = string.Empty;

            if (report)
            {
                MessageBox.Show(this,
                    _text.Format(StringId.UpdateUpToDate, UpdateService.RunningVersion),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Not ApplySettings: none of this changes the height, the reserved area or the row.
        SettingsStore.Save(_settings);
    }

    /// <summary>
    /// Shows the notice for a release the user has not been told about.
    /// </summary>
    /// <remarks>
    /// A notification rather than a dialog. The automatic check lands while the user is starting
    /// their day, and a modal window in front of that is worse than the news is good. Windows may
    /// hold the notification back entirely - a focus assist rule, or notifications turned off for
    /// this application - which is why the menu entry, not this, is what the feature relies on.
    /// </remarks>
    private void ShowUpdateNotice(string tag)
    {
        if (_trayIcon == null) return;

        _trayIcon.ShowBalloonTip(10000, Text,
            _text.Format(StringId.UpdateNotice, tag), ToolTipIcon.Info);
    }

    /// <summary>
    /// Opens the release page, or runs a check when nothing is known yet.
    /// </summary>
    private void OnUpdateMenuClicked()
    {
        if (_updateAvailableTag.Length > 0) OpenReleasePage();
        else RunUpdateCheck(report: true);
    }

    /// <summary>
    /// Opens the releases page in the user's browser.
    /// </summary>
    /// <remarks>
    /// The address is a constant in <see cref="UpdateService"/> and never a string the network
    /// answered with: this hands it to the shell, which would open whatever it was given.
    ///
    /// <c>UseShellExecute</c> is set rather than left alone. It defaults to true on .NET
    /// Framework and false on .NET, where a URL is not an executable and the call fails, so
    /// setting it serves both targets without a second code path.
    /// </remarks>
    private static void OpenReleasePage()
    {
        try
        {
            // The process is frequently null, because the shell handed the address to a browser
            // that was already running. Disposed all the same: the build treats an object created
            // and then dropped as an error.
            using (Process.Start(
                new ProcessStartInfo(UpdateService.LatestReleaseUrl) { UseShellExecute = true }))
            {
            }
        }
        catch
        {
            // No default browser, or the shell refused. There is nothing useful to say about it,
            // and the user asked to read a page, not to be told about a failure to open one.
        }
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

        StartUpdateCheckOnce();
    }

    // ---------------------------------------------------------------
    // Refreshing the tab list
    // ---------------------------------------------------------------
    /// <summary>
    /// Which window the tabs mark as the one in front, which is not always the one Windows says
    /// is in the foreground. <see cref="ActiveMark"/> holds the rule and the reasons for it.
    /// </summary>
    /// <param name="live">The windows the bar lists as of this pass.</param>
    private IntPtr WindowToMark(HashSet<IntPtr> live)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();

        switch (ActiveMark.Choose(IsOwnWindow(foreground), live.Contains(foreground),
                                  live.Contains(_markedWindow)))
        {
            case MarkChoice.TakeForeground: _markedWindow = foreground; break;
            case MarkChoice.MarkNothing: _markedWindow = IntPtr.Zero; break;
            default: break;   // KeepMarked: _markedWindow is already the answer
        }

        return _markedWindow;
    }

    /// <summary>Whether a window belongs to this application rather than somebody else.</summary>
    /// <remarks>
    /// The process rather than the handle, because the bar is not the only window this
    /// application puts on screen: the settings dialog and both menus are windows of their own,
    /// and any of them can be what a click leaves in the foreground.
    /// </remarks>
    private static bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        // A window this application may not query answers 0, which belongs to no process and so
        // is somebody else's, which is the safe reading: the mark moves rather than sticking.
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == OwnProcessId;
    }

    private void RefreshTabs()
    {
        _candidates.Clear();
        _candidates.AddRange(WindowService.EnumerateTaskWindows(Handle));

        // Before the excluded windows are dropped, so each one keeps its cache entry. Pruned
        // against the shorter list they would be forgotten and looked up again four times a
        // second, for windows that are never drawn.
        _processInfo.Forget(new HashSet<IntPtr>(_candidates));

        List<IntPtr> current = WithoutExcludedApplications(_candidates);

        // Keep the existing order and append newly opened windows at the end.
        // Membership is tested through sets: this runs every 250 ms.
        var live = new HashSet<IntPtr>(current);
        _order.RemoveAll(h => !live.Contains(h));

        // After the live set is built, because which window is marked depends on it.
        IntPtr foreground = WindowToMark(live);

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

        ArrangeByApplication();

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

    // ---------------------------------------------------------------
    // Applications the user has excluded
    // ---------------------------------------------------------------

    /// <summary>
    /// The windows that are left after the applications the user excluded are taken out.
    /// </summary>
    /// <remarks>
    /// The list itself is returned untouched while nothing is excluded, down to not reading a
    /// single process, so a bar nobody has configured behaves exactly as it did before this
    /// setting existed. That is the same bargain grouping makes.
    ///
    /// Once something is excluded the executable is asked for once per window and answered from
    /// <see cref="ProcessInfoCache"/> after that, so the 250 ms refresh costs a dictionary
    /// lookup rather than a process query.
    /// </remarks>
    private List<IntPtr> WithoutExcludedApplications(List<IntPtr> windows)
    {
        List<string> excluded = _settings.ExcludedApplications;
        if (excluded == null || excluded.Count == 0) return windows;

        var result = new List<IntPtr>(windows.Count);
        foreach (IntPtr h in windows)
        {
            if (!WindowExclusion.IsExcludedApplication(_processInfo.Name(h), excluded))
                result.Add(h);
        }

        return result;
    }

    /// <summary>
    /// Adds the application behind one window to the excluded list, and takes its windows off
    /// the bar at once.
    /// </summary>
    /// <remarks>
    /// Through the settings rather than by hiding the one tab: the user is excluding an
    /// application, so its other windows go too, and the choice is written down where it can be
    /// undone.
    /// </remarks>
    private void ExcludeApplication(IntPtr hwnd)
    {
        string name = _processInfo.Name(hwnd);
        if (name.Length == 0) return;

        if (_settings.ExcludedApplications.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;

        _settings.ExcludedApplications.Add(name);

        // The same call the settings dialog makes, so the row, the reserved area and the
        // settings file are brought up to date by one path rather than two.
        ApplySettings();
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

    /// <summary>The executables that have a window open right now, for the settings dialog.</summary>
    /// <remarks>
    /// From the candidates rather than from the row, so an application the user has just
    /// excluded is still listed. Taken from the row it would leave the list the moment it was
    /// ticked, and the tick that hid it would be the last thing the user could do to it.
    /// </remarks>
    private List<string> RunningApplications()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (IntPtr h in _candidates)
        {
            string name = _processInfo.Name(h);
            if (name.Length > 0 && seen.Add(name)) result.Add(name);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
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

        using (var pen = new Pen(_cLine))
            g.DrawLine(pen, 0, 0, ClientSize.Width, 0);

        if (_tabs.Count == 0)
        {
            // Centred in the bar rather than placed at a fixed offset, so it stays put when
            // the bar height changes.
            var emptyRect = new Rectangle(
                _metrics.Padding, _metrics.TopOffset,
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
        // The active tab's foot alone goes past the bottom of the bar, by the width of the
        // outline. The outline follows a closed path, and a bottom edge left on the last row of
        // pixels would be drawn as a line under the tab; pushed out of the client area it is not
        // drawn at all, which is what a tab standing on the edge of the bar should look like.
        // The fill does not care: everything below the bar is clipped away either way.
        Rectangle body = tab.Active
            ? Rectangle.FromLTRB(tab.Bounds.Left, tab.Bounds.Top,
                                 tab.Bounds.Right, tab.Bounds.Bottom + _metrics.ActiveOutlineWidth)
            : tab.Bounds;

        using (GraphicsPath path = RoundedTop(body, _metrics.CornerRadius))
        {
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            if (tab.Active)
            {
                using var pen = new Pen(_cTabActiveOutline, _metrics.ActiveOutlineWidth);
                g.DrawPath(pen, path);
            }

            // After the outline, so a marked tab keeps the full thickness of its accent and the
            // group still reads as one band along the top of the row. The outline is left down
            // the sides, which is where it does the marking.
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
    /// The accent along a grouped tab's top edge, clipped to the tab's own outline so it follows
    /// the rounded corners rather than squaring them off.
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
            g.FillRectangle(brush, tab.Bounds.Left, tab.Bounds.Top,
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
                g.FillRectangle(brush, x, left.Bounds.Top, width, _metrics.GroupBandHeight);
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
    /// The way between them is the strip the bar leaves above the tabs, <see cref="BarMetrics.
    /// TopOffset"/> tall, which belongs to no tab: the preview covers the top row of it and the
    /// tabs start below it, so a couple of pixels in between are on the bar and on nothing. A
    /// pointer moving fast crosses them in one message and a slow one lands in them, which is
    /// what made this show up as "it disappears if I move slowly".
    /// </remarks>
    private bool PointerIsAtPreview
    {
        get
        {
            if (_preview == null || !_preview.Visible) return false;
            if (_preview.Bounds.Contains(MousePosition)) return true;

            Point p = PointToClient(MousePosition);
            return ClientRectangle.Contains(p) && p.Y < _metrics.TopOffset;
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
        Rectangle screen = Screen.FromControl(this).Bounds;

        // No gap. The panel sits on the bar's top edge so the pointer can travel from the tab
        // onto it without crossing anything in between: a gap is desktop, and the moment the
        // pointer touched it the bar would lose its hover and take the preview down. The
        // taskbar's own thumbnails sit against it for the same reason.
        const int gap = 0;

        // As large as the box allows, and never taller than the room above the bar, which the
        // title and the frame are taken out of first. Fit is what keeps the whole panel inside
        // that room, so Place has nothing to bring back down from the top.
        int max = Scaled(PreviewMaxLogical);
        int room = Top - screen.Top - border * 2 - _preview.TitleHeight;
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
            tab.Left, tab.Width, Top, screen.Left, screen.Right, gap);

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

    /// <summary>
    /// Releases everything this form owns: the event hooks, the cached icons, the font, the
    /// tooltip, the two menus, the tray icon and the AppBar registration.
    /// </summary>
    /// <remarks>
    /// Called from two places, and <see cref="_released"/> makes the second call do nothing.
    /// <see cref="OnFormClosing"/> calls it so the desktop gets its space back as soon as the
    /// bar is closed, and <see cref="Dispose(bool)"/> calls it so nothing is left registered
    /// when the form is disposed without having been closed.
    /// </remarks>
    private void ReleaseResources()
    {
        if (_released) return;
        _released = true;

        _timer.Stop();
        _timer.Dispose();

        foreach (IntPtr hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();

        // Before the icons: the preview draws one of them, borrowed from this cache.
        _previewTimer.Stop();
        _previewTimer.Dispose();
        _preview?.Dispose();
        _preview = null;
        _previewHwnd = IntPtr.Zero;

        foreach (CachedIcon cached in _iconCache.Values)
            cached.Icon?.Dispose();
        _iconCache.Clear();

        // Each tab holds an icon the cache has just released, so the tabs go with it.
        _tabs.Clear();

        // Strings only, so there is nothing here to release. Cleared for the same reason the
        // tabs are: what it describes is gone.
        _processInfo.Clear();
        _groupIds.Clear();

        _toolTip.Dispose();

        _font?.Dispose();
        _font = null;

        _appMenu?.Dispose();
        _tabMenu?.Dispose();

        foreach (ContextMenuStrip menu in _retiredMenus)
            menu.Dispose();
        _retiredMenus.Clear();

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
    }

    /// <summary>
    /// Releases the resources once the close is settled. The base call comes first because a
    /// handler of the <c>FormClosing</c> event may cancel the close, and a bar that goes on
    /// running still needs its font, its icons and its hooks.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel) ReleaseResources();
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
