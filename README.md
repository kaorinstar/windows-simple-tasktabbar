# windows-simple-tasktabbar

[日本語版はこちら / Japanese version](README.ja.md)

A slim always-on bar that sits above the Windows taskbar and shows every open window as a
Chrome-style tab.

It does **not** merge windows into a single container. It only displays them and switches
between them.

## Why

Windows restricts which processes may bring a window to the foreground. Because of that
restriction, the window you want often stays behind the one you were just using, and the
taskbar button flashes instead of switching. This application works around that with a
three-step activation strategy — see [Reliable window activation](#reliable-window-activation).

## Behaviour

- **Click a tab** — brings that window to the front, restoring it if minimized.
- **Click the active tab** — minimizes it, the same as the Windows taskbar.
- **Click the ×, or middle-click a tab** — closes that window.
- **Drag a tab sideways** — moves it to another position. The others step aside as it passes
  them, Esc abandons the move, and dragging against either end scrolls the row.
- **Right-click a tab** — opens a menu with Close, Close other tabs, Close tabs to the left,
  Close tabs to the right and Minimize.
- **Right-click the space around the tabs** — opens a menu with Settings, Refresh and Exit.
- **Scroll the wheel over the bar, or click the arrows at either end** — moves the row when
  there are more tabs than fit. The row also follows the window you switch to.
- The tab list updates automatically when windows open, close, change title, or gain focus.
  A two-second timer runs as a safety net.

## Download

Three packages are attached to each [release](https://github.com/kaorinstar/windows-simple-tasktabbar/releases).

| Package | Size | Requirements | When to use |
|---|---|---|---|
| `WindowsSimpleTaskTabBar-net48.exe` | ~23 KB | None (Windows 10 version 1903 or later, Windows 11) | Recommended for most people |
| `WindowsSimpleTaskTabBar-net8.exe` | ~171 KB | .NET 8 Desktop Runtime (x64) | If the runtime is already installed |
| `WindowsSimpleTaskTabBar-standalone.exe` | ~69 MB | None (x64 only) | Older Windows versions |

The net48 package targets .NET Framework 4.8, which ships with Windows 10 version 1903 and
later and with Windows 11. Nothing has to be installed, and the package is a single file with
no configuration file beside it.

## Install

There is no installer.

1. Put the executable in any folder, for example `C:\Tools\WindowsSimpleTaskTabBar\`.
2. Double-click it.

The executable is not code-signed, so SmartScreen shows a warning the first time. Choose
**More info** and then **Run anyway**.

To exit, right-click the bar and choose **Exit**.

To start it with Windows, press `Windows` + `R`, enter `shell:startup`, and place a shortcut
to the executable in the folder that opens.

## Build from source

The only requirement is the .NET 8 SDK. The build can be verified on Linux and macOS as well,
although the application itself only runs on Windows.

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Producing the distributable packages:

```
# A. No runtime install required
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net48 -o artifacts/net48

# B. Small
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net8.0-windows -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/net8

# C. Standalone
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/standalone
```

## Repository layout

```
├── src/
│   ├── WindowsSimpleTaskTabBar.Core/    Logic with no UI or Windows API dependency
│   └── WindowsSimpleTaskTabBar/         The application
├── tests/
│   └── WindowsSimpleTaskTabBar.Tests/   Unit tests
├── docs/                                Design notes
└── .github/workflows/                   Continuous integration
```

See [docs/architecture.md](docs/architecture.md) for details.

## Continuous integration

Every push to `main` and every pull request runs a build and the unit tests on Windows.
Warnings are treated as errors, so a warning fails the build.

Pushing a tag such as `v0.1.0` creates a release with all three packages attached.

## Reliable window activation

Windows prevents a process that is not in the foreground from bringing a window forward. A
plain `SetForegroundWindow` call therefore fails, and the taskbar button flashes instead.

`Services/WindowService.cs` tries three strategies in order:

1. Call `SetForegroundWindow` directly.
2. If that fails, temporarily attach this thread to the input threads of both the current
   foreground window and the target window (`AttachThreadInput`), then activate.
3. If that also fails, send an Alt key press and release to lift the foreground lock, then
   activate.

The bar itself is allowed to activate when clicked; it does not use `WS_EX_NOACTIVATE`. A
process that is already in the foreground is not subject to the restriction, which raises the
success rate. The bar is thin, so the visual effect is negligible.

## Known limitations

- **Elevated applications cannot be controlled.** Windows integrity levels prevent a normal
  process from activating or closing a window owned by an elevated process. Running this
  application elevated works, but then it runs elevated permanently.
- **Primary monitor only.** Multi-monitor support is not implemented.
- **Full-screen applications cover the bar.** This is normal AppBar behaviour.
- **The order is not remembered between runs.** Tabs come back in the order Windows lists the
  windows in.
- **A tab never shrinks below its icon and the first four characters of its title.** Past that
  the row scrolls, so on a narrow screen only part of the row is visible at a time. Nothing is
  hidden: every window is still reachable by scrolling.

## Roadmap

1. Grouping by application when there are many tabs
2. Multi-monitor support
3. Window preview on hover
4. Pinned applications
5. Built-in start-with-Windows option

## Contributing

Issues and pull requests are welcome. Please keep the following in mind:

- Code, comments, and documentation are written in English. `README.ja.md` and
  `docs/architecture.ja.md` are Japanese translations kept in sync with the English originals.
- The build must pass with `-warnaserror`.
- The `net48` target must keep working. Shipping a single executable that needs no runtime
  install is a core requirement of this project.

## License

MIT
