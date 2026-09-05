using WindowsSimpleTaskTabBar.Interop;
using WindowsSimpleTaskTabBar.UI;

namespace WindowsSimpleTaskTabBar;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Prevent a second instance from starting.
        using var mutex = new Mutex(true, "WindowsSimpleTaskTabBar_SingleInstance", out bool isNew);
        if (!isNew) return;

#if NETFRAMEWORK
        // On .NET Framework the DPI awareness has to be set manually.
        // This fails on Windows 10 before version 1703, which is harmless.
        try { NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch { }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#else
        ApplicationConfiguration.Initialize();
#endif

        Application.Run(new MainForm());
    }
}
