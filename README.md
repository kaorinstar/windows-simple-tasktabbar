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
  them, Esc abandons the move, and dragging against either end scrolls the row. With grouping
  on, a tab moves within its own group, and dragging it past a neighbouring group carries the
  whole group with it. A window that is the only one of its application travels on its own.
- **Right-click a tab** — opens a menu with Close, Close other tabs, Close tabs to the left,
  Close tabs to the right and Minimize.
- **Right-click the space around the tabs** — opens a menu with Settings, Refresh and Exit.
- **Scroll the wheel over the bar, or click the arrows at either end** — moves the row when
  there are more tabs than fit. The row also follows the window you switch to.
- **Group tabs by application** — an option in the settings. Windows of one application sit
  together and share a colour along the top edge of their tabs, with a rule between one group
  and the next. A new window joins its application rather than the end of the row. Applications
  can be combined into one named group of your own, with a colour you pick. It is off until you
  turn it on.
- The tab list updates automatically when windows open, close, change title, or gain focus.
  A two-second timer runs as a safety net.
- **Switch Windows between light and dark** — the bar follows the colour setting and repaints as
  soon as it changes, including the automatic switch some people schedule.

## Download

One file is attached to each [release](https://github.com/kaorinstar/windows-simple-tasktabbar/releases):
`WindowsSimpleTaskTabBar.exe`, under 100 KB. Each release entry in
[version.md](version.md) gives the size of that release.

It targets .NET Framework 4.8, which ships with Windows 10 version 1903 and later and with
Windows 11, so nothing has to be installed. It is a single file with no configuration file
beside it, and it is built AnyCPU, so it also runs on ARM versions of Windows without
emulating x64.

Only this one package is published. A build carrying its own copy of .NET 8 would be about
69 MB and x64 only, and would help solely on versions of Windows that are themselves out of
support.

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
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net48 -o artifacts
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

Two workflows run on Windows.

`build.yml` verifies. Every push to `main` and every pull request runs a build and the unit
tests. Warnings are treated as errors, so a warning fails the build. The dependencies are checked
against the list of known vulnerabilities first, and a match fails the build too. Nothing is
packaged, and the run has read-only access to the repository.

`release.yml` distributes. Pushing a tag such as `v0.1.0` builds, tests, packages, and creates
a release with the net48 package attached. Starting the same workflow by hand produces the
package as a downloadable build artifact and creates no release, so a change to it can be
tried out before a tag is pushed.

The net8 target is built and tested on every run as well, as a second compiler over the same
source, but it is not published.

## Releasing

Releases are tagged `vMAJOR.MINOR.PATCH`, as [semantic versioning](https://semver.org/) describes:
`v0.1.0`, with no leading zeros. Below `1.0.0` the minor number rises when something is added or
changed, and the patch number when something is only fixed.

1. Rename the `## Unreleased` heading at the top of `version.md` to the new version, and do the
   same in `version.ja.md`. Entries are written under that heading as each change lands, so the
   list should already be there; add anything missing, and write it as what changed for someone
   using the application rather than which pull requests were merged.
2. Commit both files to `main`.
3. Tag that commit and push the tag:

   ```
   git tag v0.1.0
   git push origin v0.1.0
   ```

The release workflow then builds, tests, packages the executable and creates the release, using
that section of `version.md` as the release notes. The executable is stamped with the version from
the tag, so its file properties in Windows say which release it came from. A tag in the wrong
format, or one with no section in `version.md`, fails the workflow before anything is built, so no
half-finished release is published.

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

- **A window owned by an elevated application cannot be fully controlled.** Windows integrity
  levels stop a normal process from acting on one. Switching to a window that is already on
  screen usually works, because the bar is in the foreground by the time it asks; restoring one
  from minimized does not, and neither does closing it, and Windows reports no error either way.
  Task Manager is the example most people meet, because it elevates itself on an administrator
  account. Running this application elevated removes the limit, but then it runs elevated
  permanently.
- **Primary monitor only.** Multi-monitor support is not implemented.
- **Full-screen applications cover the bar.** This is normal AppBar behaviour.
- **The order is not remembered between runs.** Tabs come back in the order Windows lists the
  windows in. Turning grouping off leaves the tabs where grouping put them rather than restoring
  the order they opened in, which is not recorded anywhere.
- **Two different applications with the same executable name share a group.** Applications are
  matched on the file name, because that is what people recognise, so two unrelated programs
  both installed as `app.exe` are treated as one.
- **A tab never shrinks below its icon and the first four characters of its title.** Past that
  the row scrolls, so on a narrow screen only part of the row is visible at a time. Nothing is
  hidden: every window is still reachable by scrolling.

## Roadmap

1. Multi-monitor support
2. Window preview on hover
3. Pinned applications
4. Built-in start-with-Windows option

## Contributing

Issues and pull requests are welcome. Please keep the following in mind:

- Code, comments, and documentation are written in English. `README.ja.md` and
  `docs/architecture.ja.md` are Japanese translations kept in sync with the English originals.
- The build must pass with `-warnaserror`.
- The `net48` target must keep working. Shipping a single executable that needs no runtime
  install is a core requirement of this project.

## License

MIT
