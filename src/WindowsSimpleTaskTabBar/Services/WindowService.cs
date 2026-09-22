using System.Runtime.InteropServices;
using System.Text;
using WindowsSimpleTaskTabBar.Core.Filtering;
using WindowsSimpleTaskTabBar.Interop;

namespace WindowsSimpleTaskTabBar.Services;

/// <summary>
/// Enumerates open windows and performs operations on them.
/// </summary>
internal static class WindowService
{
    /// <summary>This application's own process, which owns no tab.</summary>
    private static readonly uint OwnProcessId = NativeMethods.GetCurrentProcessId();

    /// <summary>
    /// Returns the windows that should be shown as tabs.
    /// </summary>
    /// <remarks>
    /// This application's own windows are left out by process rather than by handle. There is
    /// one bar per monitor now, so there is no single handle to exclude, and the settings
    /// dialog is a window of its own as well.
    /// </remarks>
    public static List<IntPtr> EnumerateTaskWindows()
    {
        var result = new List<IntPtr>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsOwnWindow(hwnd)) return true;
            if (IsTaskWindow(hwnd)) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>Whether a window belongs to this application rather than somebody else.</summary>
    public static bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        // A window this application may not query answers 0, which belongs to no process and
        // so is somebody else's.
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == OwnProcessId;
    }

    /// <summary>
    /// The rectangle that says which monitor a window belongs to: where it is drawn, or where
    /// it comes back to when it is minimized.
    /// </summary>
    /// <remarks>
    /// Windows parks a minimized window at roughly (-32000, -32000), which is on no monitor and
    /// nearest to whichever one reaches furthest towards the top left. Asking where such a
    /// window is drawn would put its tab on a bar it has nothing to do with, so
    /// <c>rcNormalPosition</c> is read instead, which is where it restores onto.
    ///
    /// A call that fails leaves a rectangle of zeros, which lands on whichever monitor holds the
    /// origin. That is the primary monitor on an ordinary desktop, so a window nothing can be
    /// read about still gets a tab somewhere rather than none at all.
    /// </remarks>
    public static void RestoredBounds(IntPtr hwnd, out int left, out int top,
                                      out int right, out int bottom)
    {
        NativeMethods.RECT rect = default;

        if (NativeMethods.IsIconic(hwnd))
        {
            var placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();

            if (NativeMethods.GetWindowPlacement(hwnd, ref placement))
                rect = placement.rcNormalPosition;
        }
        else
        {
            NativeMethods.GetWindowRect(hwnd, out rect);
        }

        left = rect.left;
        top = rect.top;
        right = rect.right;
        bottom = rect.bottom;
    }

    private static bool IsTaskWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return false;
        if (NativeMethods.GetWindowTextLengthW(hwnd) == 0) return false;

        long exStyle = NativeMethods.GetWindowLongSafe(hwnd, NativeMethods.GWL_EXSTYLE);

        // Skip tool windows, unless they explicitly ask to appear on the taskbar.
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
            return false;

        // Skip cloaked windows, which is how hidden Store apps appear.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
            return false;

        // The shell's own windows only. What the user excludes is an application, which needs
        // the process behind the window and is settled in BarHost, where the answers are cached.
        return !WindowExclusion.IsShellWindow(GetClassName(hwnd));
    }

    public static string GetTitle(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// The window class a packaged application is shown in. The frame belongs to
    /// ApplicationFrameHost, so the application itself has to be looked for among its children.
    /// </summary>
    private const string PackagedAppFrameClass = "ApplicationFrameWindow";

    /// <summary>
    /// Full path of the executable that owns a window, or an empty string when it cannot be
    /// read.
    /// </summary>
    /// <remarks>
    /// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> rather than what <c>Process.MainModule</c>
    /// needs: that asks for PROCESS_VM_READ, which is refused for an elevated process and for a
    /// process of a different bitness, and answers with an exception rather than a result.
    ///
    /// The process handle is closed in a finally rather than held by a SafeHandle. It does not
    /// outlive this method, and a SafeHandle subclass would be one more type for the disposal
    /// analyzers to have an opinion about. See the ownership table in docs/architecture.md.
    /// </remarks>
    public static string GetExecutablePath(IntPtr hwnd)
    {
        uint pid = OwningProcessId(hwnd);
        if (pid == 0) return string.Empty;

        IntPtr process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return string.Empty;

        try
        {
            // Larger than MAX_PATH on purpose. A path longer than the buffer is not truncated:
            // the call fails, and the window would silently lose its group.
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return NativeMethods.QueryFullProcessImageNameW(process, 0, sb, ref size)
                ? sb.ToString()
                : string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>
    /// The process a window's tab should be grouped under.
    /// </summary>
    /// <remarks>
    /// A packaged application - Calculator, Settings, Photos - is drawn in a frame owned by
    /// ApplicationFrameHost.exe rather than by the application. Asked directly, every one of
    /// them would answer with the same executable and land in one group. The application owns a
    /// child of the frame, so the first child belonging to a different process is asked instead.
    /// </remarks>
    private static uint OwningProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return 0;

        if (!string.Equals(GetClassName(hwnd), PackagedAppFrameClass, StringComparison.Ordinal))
            return pid;

        uint frame = pid;
        uint inner = 0;
        NativeMethods.EnumChildWindows(hwnd, (child, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(child, out uint childPid);
            if (childPid == 0 || childPid == frame) return true;

            inner = childPid;
            return false;
        }, IntPtr.Zero);

        return inner != 0 ? inner : pid;
    }

    /// <summary>
    /// Brings the given window to the foreground as reliably as possible.
    /// Windows restricts foreground changes, so three strategies are tried in order.
    /// </summary>
    public static void Activate(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;

        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        else
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

        // Strategy 1: try the plain call first.
        if (NativeMethods.SetForegroundWindow(hwnd) && NativeMethods.GetForegroundWindow() == hwnd)
            return;

        // Strategy 2: temporarily attach to the input thread of the foreground window.
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

        bool attachedForeground = false;
        bool attachedTarget = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedForeground = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                attachedTarget = NativeMethods.AttachThreadInput(currentThread, targetThread, true);

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget) NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            if (attachedForeground) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }

        if (NativeMethods.GetForegroundWindow() == hwnd) return;

        // Strategy 3: send an Alt key press and release to lift the foreground lock.
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    public static void Minimize(IntPtr hwnd)
    {
        if (NativeMethods.IsWindow(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
    }

    public static void Close(IntPtr hwnd)
    {
        if (NativeMethods.IsWindow(hwnd))
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Gets the icon of a window, or null when no icon is available.
    /// A timeout is used so an unresponsive application cannot block the bar.
    /// </summary>
    public static Icon GetWindowIcon(IntPtr hwnd)
    {
        IntPtr handle = SendGetIcon(hwnd, NativeMethods.ICON_SMALL2);
        if (handle == IntPtr.Zero) handle = SendGetIcon(hwnd, NativeMethods.ICON_SMALL);
        if (handle == IntPtr.Zero) handle = SendGetIcon(hwnd, NativeMethods.ICON_BIG);
        if (handle == IntPtr.Zero) handle = NativeMethods.GetClassLongSafe(hwnd, NativeMethods.GCLP_HICONSM);
        if (handle == IntPtr.Zero) handle = NativeMethods.GetClassLongSafe(hwnd, NativeMethods.GCLP_HICON);
        if (handle == IntPtr.Zero) return null;

        try
        {
            using var source = Icon.FromHandle(handle);
            return (Icon)source.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr SendGetIcon(IntPtr hwnd, int iconType)
    {
        NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_GETICON, new IntPtr(iconType), IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG, 120, out IntPtr result);
        return result;
    }
}
