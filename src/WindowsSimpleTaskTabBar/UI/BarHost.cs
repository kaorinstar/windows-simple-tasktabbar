using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Win32;
using WindowsSimpleTaskTabBar.Core.Filtering;
using WindowsSimpleTaskTabBar.Core.Focus;
using WindowsSimpleTaskTabBar.Core.Localization;
using WindowsSimpleTaskTabBar.Core.Monitors;
using WindowsSimpleTaskTabBar.Core.Settings;
using WindowsSimpleTaskTabBar.Core.Update;
using WindowsSimpleTaskTabBar.Interop;
using WindowsSimpleTaskTabBar.Services;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// The application itself: everything there is one of, however many bars are on screen.
/// </summary>
/// <remarks>
/// <see cref="MainForm"/> was both the bar and the application until a bar was needed on every
/// monitor (#62). Several of it could not simply be created, because some of what it held has to
/// exist once per process rather than once per bar: the tray icon, the settings and their
/// dialog, the caches, the event hooks and the timer that drives everything.
///
/// Those moved here, and <see cref="MainForm"/> is now one bar and nothing else. The split is
/// along the line between what the user has one of and what each screen has one of: the host
/// decides which windows exist and which monitor each one is on, and each bar decides how its
/// own row of them is ordered, laid out and drawn.
///
/// An <see cref="ApplicationContext"/> rather than a form kept hidden for the purpose, because
/// the application no longer has a main window: every bar can be taken away and brought back by
/// a monitor being unplugged, and the one that closes the application is the menu, not a window.
///
/// One thread of control, and it is <see cref="_timer"/>. Windows events set a flag, a display
/// change sets a flag, an update check leaves its answer in a field, and the next tick acts on
/// all of them on the user interface thread. Nothing here is posted to a window handle, because
/// which handles exist is exactly what changes when a monitor is unplugged.
/// </remarks>
internal sealed class BarHost : ApplicationContext
{
    /// <summary>What the menus, the tray icon and the message boxes are titled.</summary>
    private const string AppName = "WindowsSimpleTaskTabBar";

    // A window icon, with the time it was fetched. See IconMaxAgeMs.
    private sealed class CachedIcon
    {
        public Icon Icon;
        public int FetchedAt;
    }

    // An application can change its icon while running, so a cached icon is fetched
    // again once it is older than this. Only one icon is refreshed per pass, because
    // WM_GETICON blocks until the owning window answers or the timeout expires.
    private const int IconMaxAgeMs = 10_000;

    /// <summary>A monitor, as the bars are built from it.</summary>
    /// <remarks>
    /// The device name is what a bar is matched to its monitor by from one display change to
    /// the next, rather than the position, which is the thing that changes when a monitor is
    /// moved in the display settings.
    /// </remarks>
    private readonly struct MonitorPlace
    {
        public MonitorPlace(string device, Rectangle bounds)
        {
            Device = device;
            Bounds = bounds;
        }

        public string Device { get; }

        public Rectangle Bounds { get; }
    }

    private readonly AppSettings _settings;

    /// <summary>The interface text, in the language the settings ask for.</summary>
    private UiText _text;

    private readonly ProcessInfoCache _processInfo = new();
    private readonly Dictionary<IntPtr, CachedIcon> _iconCache = new();

    /// <summary>The bars on screen, primary monitor first.</summary>
    private readonly List<MainForm> _bars = new();

    // Every window that could be a tab, including the ones the user excludes. The bars hold
    // what is shown; this holds what was offered, so the settings dialog can still list an
    // application whose windows it has just taken off the bar, and so the process cache keeps
    // its entry for a window that is looked at on every refresh but never drawn.
    private readonly List<IntPtr> _candidates = new();

    /// <summary>The windows of one monitor, reused between passes rather than rebuilt.</summary>
    private readonly List<List<IntPtr>> _byMonitor = new();
    private readonly List<MonitorBox> _monitorBoxes = new();

    private readonly System.Windows.Forms.Timer _timer = new();
    private NativeMethods.WinEventDelegate _winEventProc;   // kept in a field so it is not collected
    private readonly List<IntPtr> _hooks = new();
    private bool _dirty = true;
    private int _tickCount;

    /// <summary>
    /// Set from the thread <see cref="SystemEvents"/> raises its events on, and read by the
    /// timer.
    /// </summary>
    /// <remarks>
    /// Volatile rather than locked: one bool, written by one thread and read by another, where
    /// a tick's delay in seeing it costs a quarter of a second before the bars are rebuilt.
    /// The work itself cannot be done there, because creating and destroying windows belongs on
    /// the thread that owns them.
    /// </remarks>
    private volatile bool _displayChanged;

    // The window the tabs mark as the one in front. Not always the foreground window: see
    // WindowToMark, and Core/Focus/ActiveMark.cs for the rule itself.
    private IntPtr _markedWindow;

    // The priority order the rows were last put into, and whether they still have to be. The
    // bars start with the whole row to arrange, and a change to the list arranges them again;
    // between those two moments priority decides where a new tab is inserted and nothing else,
    // so that a tab the user has dragged stays where they put it.
    private List<string> _appliedPriority;

    private NotifyIcon _trayIcon;
    private IntPtr _trayIconHandle;   // owned by this class; see LoadSmallApplicationIcon

    // The menu the bars show for the space around their tabs. One instance serves every bar:
    // only one of them can be showing a menu at a time. The tray icon has its own, because a
    // NotifyIcon keeps the one it is given.
    private ContextMenuStrip _appMenu;

    // Menus replaced by a change of language, kept until the application closes. See
    // RebuildMenus.
    private readonly List<ContextMenuStrip> _retiredMenus = new();

    // Only a record of which dialog is open, so a second request can bring it forward. The
    // using statement in ShowSettings owns it, and this field is null again by the time that
    // statement ends.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the using statement in ShowSettings.")]
    private SettingsForm _settingsForm;

    // The update check. _updateAvailableTag names a release newer than this build, and is what
    // both menus read when they open. It is seeded from the settings file at startup and set
    // again by each check; it is empty when nothing newer is known.
    private bool _updateCheckStarted;
    private bool _updateCheckRunning;
    private string _updateAvailableTag = string.Empty;

    // Where a check leaves its answer for the timer to pick up. Written on a pool thread and
    // read on the user interface thread, so both sides take the lock.
    private readonly object _updateGate = new();
    private UpdateCheckResult _updateAnswer;
    private bool _updateAnswerAsked;

    private bool _released;   // see ReleaseResources

    public BarHost()
    {
        _settings = SettingsStore.Load();

        // The rows are empty until the first refresh, so putting them in priority order costs
        // nothing there. Remembering the list here is what stops the first settings change of
        // the session - a colour, a height - from being read as a change to the order and
        // reordering tabs the user had dragged.
        _appliedPriority = new List<string>(_settings.ApplicationPriority);

        _text = TextForSettings();
        _updateAvailableTag = KnownNewerRelease();

        _appMenu = BuildMenu();
        CreateTrayIcon();

        _timer.Interval = 250;
        _timer.Tick += (_, __) => OnTimerTick();
    }

    /// <summary>
    /// Puts the bars on screen and starts watching for windows, display changes and releases.
    /// </summary>
    /// <remarks>
    /// Separate from the constructor so that what is built and what is started are two steps:
    /// the timer below only ticks once <see cref="Application.Run(ApplicationContext)"/> is
    /// pumping messages, and the context has to exist before it can be handed to that call.
    /// </remarks>
    public void Start()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        SyncBars();
        RegisterHooks();
        Refresh();
        _timer.Start();
    }

    // ---------------------------------------------------------------
    // What a bar asks the application for
    // ---------------------------------------------------------------

    /// <summary>The settings, which every bar reads and none of them owns.</summary>
    public AppSettings Settings => _settings;

    /// <summary>The interface text, in the language the settings ask for.</summary>
    public UiText Text => _text;

    /// <summary>Which executable owns a window, shared so it is looked up once.</summary>
    public ProcessInfoCache ProcessInfo => _processInfo;

    /// <summary>
    /// The icon of a window, fetched once and shared by whichever bar is showing it.
    /// </summary>
    /// <remarks>
    /// The cache is here rather than in the bar so that a window dragged from one monitor to
    /// another keeps its icon instead of being asked for it again on arrival. The bars borrow
    /// what they are given and never dispose it; <see cref="ReleaseResources"/> does.
    /// </remarks>
    public Icon IconFor(IntPtr hwnd)
    {
        if (_iconCache.TryGetValue(hwnd, out CachedIcon cached)) return cached.Icon;

        cached = new CachedIcon
        {
            Icon = WindowService.GetWindowIcon(hwnd),
            FetchedAt = Environment.TickCount,
        };
        _iconCache[hwnd] = cached;
        return cached.Icon;
    }

    /// <summary>
    /// Asks for a refresh on the next tick, after something that changes what the bars hold.
    /// </summary>
    public void MarkDirty()
    {
        _dirty = true;
    }

    /// <summary>Refreshes every bar now, for the menu entry that asks for it.</summary>
    public void RefreshNow()
    {
        _dirty = false;
        Refresh();
    }

    /// <summary>Shows the application menu over one of the bars.</summary>
    public void ShowApplicationMenu(MainForm bar, Point point)
    {
        _appMenu.Show(bar, point);
    }

    /// <summary>
    /// Opens the settings dialog. Only one at a time, and changes take effect as they are made.
    /// </summary>
    public void ShowSettings()
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
    /// Adds the application behind one window to the excluded list, and takes its windows off
    /// the bars at once.
    /// </summary>
    /// <remarks>
    /// Through the settings rather than by hiding the one tab: the user is excluding an
    /// application, so its other windows go too, wherever they are, and the choice is written
    /// down where it can be undone.
    /// </remarks>
    public void ExcludeApplication(IntPtr hwnd)
    {
        string name = _processInfo.Name(hwnd);
        if (name.Length == 0) return;

        if (_settings.ExcludedApplications.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;

        _settings.ExcludedApplications.Add(name);

        // The same call the settings dialog makes, so the rows, the reserved areas and the
        // settings file are brought up to date by one path rather than two.
        ApplySettings();
    }

    /// <summary>
    /// Ends the application: every bar, every AppBar registration and the tray icon go.
    /// </summary>
    /// <remarks>
    /// Called by the Exit entry in either menu, and by a bar that Windows closed underneath the
    /// application, which is how closing one bar ends the whole of it.
    /// </remarks>
    public void RequestExit()
    {
        if (_released) return;

        ReleaseResources();
        ExitThread();
    }

    /// <summary>
    /// One bar has been closed by something other than the application: the user, or Windows
    /// ending the session. Closing one bar closes all of them.
    /// </summary>
    /// <remarks>
    /// Taken off the list first, because it is already closing and must not be closed again
    /// from inside its own <c>FormClosing</c>.
    /// </remarks>
    public void BarClosed(MainForm bar)
    {
        _bars.Remove(bar);
        RequestExit();
    }

    // ---------------------------------------------------------------
    // One bar per monitor
    // ---------------------------------------------------------------

    /// <summary>
    /// Brings the bars into line with the monitors that exist: one appears for a monitor that
    /// was plugged in, one goes for a monitor that was unplugged, and one that stayed is moved
    /// to where its monitor now is.
    /// </summary>
    /// <remarks>
    /// Removing a bar takes its AppBar registration with it, in
    /// <see cref="MainForm.ReleaseResources"/>. A registration left behind reserves a strip of a
    /// desktop that no longer has anything to give it back.
    ///
    /// Idempotent, so it can be called from a display change, from the setting, and from a bar
    /// that saw WM_DISPLAYCHANGE, without any of them having to know about the others.
    /// </remarks>
    private void SyncBars()
    {
        if (_released) return;

        List<MonitorPlace> places = MonitorsToCarryABar();

        // Bars whose monitor is gone. Taken off the list first, so nothing below can hand one
        // of them a window.
        for (int i = _bars.Count - 1; i >= 0; i--)
        {
            if (places.Exists(p => p.Device == _bars[i].Device)) continue;

            MainForm bar = _bars[i];
            _bars.RemoveAt(i);
            bar.CloseForHost();
        }

        for (int i = 0; i < places.Count; i++)
        {
            MonitorPlace place = places[i];
            int at = _bars.FindIndex(b => b.Device == place.Device);

            if (at < 0)
            {
                var bar = new MainForm(this, place.Device, place.Bounds);
                _bars.Insert(Math.Min(i, _bars.Count), bar);

                // Built and placed, not shown. The refresh that follows every call to this
                // method hands it its windows, and the bar puts itself on screen with them.
                bar.Prepare();
                continue;
            }

            // Kept in the order the monitors are in, so the primary monitor's bar stays first.
            if (at != i)
            {
                MainForm bar = _bars[at];
                _bars.RemoveAt(at);
                _bars.Insert(i, bar);
            }

            _bars[i].MoveToMonitor(place.Bounds);
        }
    }

    /// <summary>
    /// The monitors that carry a bar, the primary one first.
    /// </summary>
    /// <remarks>
    /// The primary monitor leads the list for two reasons: its bar is the one that exists in
    /// either setting, and <see cref="MonitorMap.Owner"/> gives an evenly split window to the
    /// earlier monitor, which should be the one the user thinks of as theirs.
    /// </remarks>
    private List<MonitorPlace> MonitorsToCarryABar()
    {
        var places = new List<MonitorPlace>();

        Screen[] screens = Screen.AllScreens;
        if (screens == null || screens.Length == 0) return places;

        // The first screen rather than nothing when Windows names no primary monitor, which it
        // can do while displays are being reconnected. One bar somewhere is better than a
        // setting that quietly leaves the user with none.
        Screen primary = Screen.PrimaryScreen ?? screens[0];
        places.Add(new MonitorPlace(primary.DeviceName, primary.Bounds));

        if (_settings.Monitors == MonitorMode.PrimaryOnly) return places;

        foreach (Screen screen in screens)
        {
            if (screen.DeviceName == primary.DeviceName) continue;
            places.Add(new MonitorPlace(screen.DeviceName, screen.Bounds));
        }

        return places;
    }

    /// <summary>
    /// Acted on by the next tick rather than here: this runs on the thread
    /// <see cref="SystemEvents"/> raises its events on.
    /// </summary>
    private void OnDisplaySettingsChanged(object sender, EventArgs e)
    {
        _displayChanged = true;
    }

    /// <summary>
    /// A bar saw WM_DISPLAYCHANGE. Windows sends it for changes
    /// <see cref="SystemEvents.DisplaySettingsChanged"/> does not always report.
    /// </summary>
    public void OnDisplayChanged()
    {
        _displayChanged = true;
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

    /// <summary>
    /// The one place work is started from, four times a second.
    /// </summary>
    /// <remarks>
    /// A window moved from one monitor to another needs no event of its own: the refresh below
    /// looks up where every window is on every pass, so the tab changes bars within 250 ms.
    /// </remarks>
    private void OnTimerTick()
    {
        _tickCount++;

        if (_displayChanged)
        {
            _displayChanged = false;
            SyncBars();
            _dirty = true;
        }

        // Refresh on change, plus every two seconds as a safety net.
        if (_dirty || _tickCount % 8 == 0)
        {
            _dirty = false;
            Refresh();
        }

        TakeUpdateAnswer();
        StartUpdateCheckOnce();
    }

    // ---------------------------------------------------------------
    // Refreshing the bars
    // ---------------------------------------------------------------

    /// <summary>
    /// Looks up every window once, works out which monitor each one is on, and hands each bar
    /// the windows that belong to it.
    /// </summary>
    /// <remarks>
    /// Once for the whole application rather than once per bar. Enumerating the windows, asking
    /// for the executable behind each one and fetching icons are the expensive parts, and none
    /// of them has a different answer on a second monitor.
    /// </remarks>
    private void Refresh()
    {
        if (_released) return;

        _candidates.Clear();
        _candidates.AddRange(WindowService.EnumerateTaskWindows());

        // Before the excluded windows are dropped, so each one keeps its cache entry. Pruned
        // against the shorter list they would be forgotten and looked up again four times a
        // second, for windows that are never drawn.
        _processInfo.Forget(new HashSet<IntPtr>(_candidates));

        List<IntPtr> current = WithoutExcludedApplications(_candidates);
        var live = new HashSet<IntPtr>(current);

        // After the live set is built, because which window is marked depends on it.
        IntPtr marked = WindowToMark(live);

        ForgetIconsOf(live);

        // Before the rows are rebuilt, so no tab holds an icon that is about to be replaced.
        RefreshStalestIcon(current);

        Deal(current);

        for (int i = 0; i < _bars.Count; i++) _bars[i].SetWindows(_byMonitor[i], marked);
    }

    /// <summary>
    /// Sorts the windows into one list per bar, by the monitor each window is on.
    /// </summary>
    /// <remarks>
    /// A single bar takes every window, wherever it is. That is the point of the
    /// <see cref="MonitorMode.PrimaryOnly"/> setting: it is the only bar there is, and a window
    /// it left out would have nothing to reach it from.
    /// </remarks>
    private void Deal(List<IntPtr> windows)
    {
        while (_byMonitor.Count < _bars.Count) _byMonitor.Add(new List<IntPtr>());
        for (int i = 0; i < _byMonitor.Count; i++) _byMonitor[i].Clear();

        if (_bars.Count == 0) return;

        if (_bars.Count == 1)
        {
            _byMonitor[0].AddRange(windows);
            return;
        }

        _monitorBoxes.Clear();
        foreach (MainForm bar in _bars)
        {
            Rectangle bounds = bar.Monitor;
            _monitorBoxes.Add(new MonitorBox(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
        }

        foreach (IntPtr hwnd in windows)
        {
            WindowService.RestoredBounds(hwnd, out int left, out int top, out int right, out int bottom);

            int monitor = MonitorMap.Owner(left, top, right, bottom, _monitorBoxes);
            if (monitor >= 0) _byMonitor[monitor].Add(hwnd);
        }
    }

    /// <summary>Releases the icons of windows that are no longer listed.</summary>
    private void ForgetIconsOf(HashSet<IntPtr> live)
    {
        foreach (IntPtr key in _iconCache.Keys.ToList())
        {
            if (live.Contains(key)) continue;

            _iconCache[key].Icon?.Dispose();
            _iconCache.Remove(key);
        }
    }

    /// <summary>
    /// Fetches the single oldest cached icon again, if it has passed <see cref="IconMaxAgeMs"/>.
    /// Only one per pass: the underlying WM_GETICON call blocks until the owning window
    /// answers or times out, and this runs on the user interface thread.
    /// </summary>
    private void RefreshStalestIcon(List<IntPtr> windows)
    {
        int now = Environment.TickCount;
        IntPtr stalest = IntPtr.Zero;
        int oldest = IconMaxAgeMs;

        foreach (IntPtr h in windows)
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

        // The bars are holding the icon that is being replaced, so they are told to let go of
        // it before it is disposed. The refresh that follows hands them the new one.
        foreach (MainForm bar in _bars) bar.ForgetIcon(stalest);
        previous?.Dispose();
    }

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
    /// Which window the tabs mark as the one in front, which is not always the one Windows says
    /// is in the foreground. <see cref="ActiveMark"/> holds the rule and the reasons for it.
    /// </summary>
    /// <remarks>
    /// One answer for the whole application, not one per bar: there is a single foreground
    /// window on the desktop, and the bar whose row holds it is the one that marks a tab.
    /// </remarks>
    /// <param name="live">The windows the bars list as of this pass.</param>
    private IntPtr WindowToMark(HashSet<IntPtr> live)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();

        switch (ActiveMark.Choose(WindowService.IsOwnWindow(foreground), live.Contains(foreground),
                                  live.Contains(_markedWindow)))
        {
            case MarkChoice.TakeForeground: _markedWindow = foreground; break;
            case MarkChoice.MarkNothing: _markedWindow = IntPtr.Zero; break;
            default: break;   // KeepMarked: _markedWindow is already the answer
        }

        return _markedWindow;
    }

    /// <summary>The executables that have a window open right now, for the settings dialog.</summary>
    /// <remarks>
    /// From the candidates rather than from the rows, so an application the user has just
    /// excluded is still listed. Taken from a row it would leave the list the moment it was
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

    // ---------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------

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
    /// Applies a settings change and writes it out. A failed write is not reported: losing a
    /// preference is not worth interrupting the user for.
    /// </summary>
    private void ApplySettings()
    {
        // In place, so the bars read the same values the file will hold. A name the settings
        // dialog could not keep, or an application claimed by two groups, is settled here rather
        // than only on the way out.
        _settings.Normalize();

        // A change to the priority order puts each whole row into it once, on the refresh below.
        // Every other setting leaves the rows as they are, so a tab the user dragged is not
        // pulled back because they went on to change the colours.
        bool resort = PriorityChanged();

        // Before the bars are told, which build their fonts from it: the family comes from the
        // language.
        UiText text = TextForSettings();
        bool languageChanged = text.Language != _text.Language;
        if (languageChanged)
        {
            _text = text;
            RebuildMenus();
        }

        // The monitor setting adds or removes a bar, so it is settled before the bars that
        // remain are asked to lay themselves out again.
        SyncBars();

        foreach (MainForm bar in _bars) bar.ApplySettingsChanged(resort, languageChanged);

        // Grouping is applied while a row is rebuilt, so a change to it shows on this refresh
        // rather than up to two seconds later. The dialog says changes apply straight away.
        RefreshNow();

        SettingsStore.Save(_settings);
    }

    /// <summary>
    /// Whether the priority order differs from the one the rows were last put into, and
    /// remembers the current one either way.
    /// </summary>
    private bool PriorityChanged()
    {
        List<string> priority = _settings.ApplicationPriority ?? new List<string>();

        if (_appliedPriority.Count == priority.Count)
        {
            bool same = true;
            for (int i = 0; i < priority.Count; i++)
            {
                if (string.Equals(_appliedPriority[i], priority[i], StringComparison.Ordinal))
                    continue;

                same = false;
                break;
            }

            if (same) return false;
        }

        _appliedPriority = new List<string>(priority);
        return true;
    }

    // ---------------------------------------------------------------
    // Menus and the tray icon
    // ---------------------------------------------------------------

    /// <summary>
    /// Builds the application menu. The bars and the tray icon each need their own
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
        menu.Items.Add(_text[StringId.MenuRefresh], null, (_, __) => RefreshNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_text[StringId.MenuExit], null, (_, __) => RequestExit());

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
    /// Builds the application menus again after the language has changed. Each bar rebuilds its
    /// own tab menu.
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
        _appMenu = BuildMenu();

        if (_trayIcon != null)
        {
            // The NotifyIcon does not own its menu, so the one it held is retired here too.
            Retire(_trayIcon.ContextMenuStrip);
            _trayIcon.ContextMenuStrip = BuildMenu();
        }
    }

    /// <summary>Keeps a menu that is no longer shown, to be disposed when the application closes.</summary>
    private void Retire(ContextMenuStrip menu)
    {
        if (menu != null) _retiredMenus.Add(menu);
    }

    /// <summary>
    /// Puts an icon in the notification area. Without it the only way to reach the menu is
    /// to right-click a bar, which is also where the tabs are.
    /// </summary>
    private void CreateTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = LoadSmallApplicationIcon(),
            Text = AppName,
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

    // ---------------------------------------------------------------
    // Telling the user a new version exists
    // ---------------------------------------------------------------

    /// <summary>The name the update entry is found by in both menus.</summary>
    private const string UpdateItemName = "update";

    /// <summary>
    /// How long after the bars appear the automatic check runs, in timer ticks of 250 ms.
    /// </summary>
    /// <remarks>
    /// Ten seconds. The application is usually started at logon, where the network is often not
    /// up yet when the first window is drawn; checking immediately would fail for a reason that
    /// has nothing to do with whether a release exists. Waiting also keeps the request off the
    /// path that puts the bars on screen.
    /// </remarks>
    private const int UpdateCheckDelayTicks = 40;

    /// <summary>
    /// The release the user was last told about, when it is still newer than this build.
    /// </summary>
    /// <remarks>
    /// The check runs at most once a day, so an application started again the same day runs
    /// none, and without this the menu would fall back to "Check for updates..." while a newer
    /// release was sitting in the settings file. The notice itself is still shown once per
    /// release; this is only what the menu says.
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
    /// Runs a check on a pool thread and leaves the answer for the timer to pick up.
    /// </summary>
    /// <remarks>
    /// Left in a field rather than posted to a window, because the application owns no window of
    /// its own and the bars it does own can be taken away by a monitor being unplugged while the
    /// request is in flight. The answer waits at most one tick, which is a quarter of a second
    /// after a request that took a network round trip.
    /// </remarks>
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

            lock (_updateGate)
            {
                _updateAnswer = result;
                _updateAnswerAsked = report;
            }
        });
    }

    /// <summary>Acts on a check that has come back, on the user interface thread.</summary>
    private void TakeUpdateAnswer()
    {
        UpdateCheckResult result;
        bool report;

        lock (_updateGate)
        {
            if (_updateAnswer == null) return;

            result = _updateAnswer;
            report = _updateAnswerAsked;
            _updateAnswer = null;
        }

        _updateCheckRunning = false;
        OnUpdateChecked(result, report);
    }

    private void OnUpdateChecked(UpdateCheckResult result, bool report)
    {
        if (_released) return;

        if (result.Outcome == UpdateCheckOutcome.Failed)
        {
            // The time is not recorded: a machine that was offline at logon should try again on
            // the next start rather than wait another day.
            if (report) Say(_text[StringId.UpdateCheckFailed]);
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
                if (Ask(_text.Format(StringId.UpdateAvailableAsk, result.LatestTag)))
                    OpenReleasePage();
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

            if (report) Say(_text.Format(StringId.UpdateUpToDate, UpdateService.RunningVersion));
        }

        // Not ApplySettings: none of this changes a height, a reserved area or a row.
        SettingsStore.Save(_settings);
    }

    /// <summary>
    /// The bar a dialog belongs in front of, or null when there is none to stand on.
    /// </summary>
    /// <remarks>
    /// The primary monitor's bar, which is the first in the list. Every bar can be gone at the
    /// moment a check comes back - the monitor setting, or an unplugged monitor - and a dialog
    /// with no owner is still shown.
    /// </remarks>
    private IWin32Window Owner => _bars.Count > 0 ? _bars[0] : null;

    private void Say(string message)
    {
        MessageBox.Show(Owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool Ask(string question)
    {
        return MessageBox.Show(Owner, question, AppName,
            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
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

        _trayIcon.ShowBalloonTip(10000, AppName,
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
    // Cleanup
    // ---------------------------------------------------------------

    /// <summary>
    /// Releases everything the application owns: the bars and their AppBar registrations, the
    /// event hooks, the cached icons, the menus and the tray icon.
    /// </summary>
    private void ReleaseResources()
    {
        if (_released) return;
        _released = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        _timer.Stop();
        _timer.Dispose();

        foreach (IntPtr hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();

        // Before the icons, which the bars are drawing with.
        foreach (MainForm bar in _bars.ToList()) bar.CloseForHost();
        _bars.Clear();

        foreach (CachedIcon cached in _iconCache.Values)
            cached.Icon?.Dispose();
        _iconCache.Clear();

        // Strings only, so there is nothing here to release. Cleared for the same reason the
        // icons are: what it describes is gone.
        _processInfo.Clear();
        _candidates.Clear();

        _appMenu?.Dispose();
        _appMenu = null;

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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseResources();
        base.Dispose(disposing);
    }
}
