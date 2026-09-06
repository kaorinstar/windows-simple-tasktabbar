# Architecture

[日本語版はこちら / Japanese version](architecture.ja.md)

## Repository layout

```
windows-simple-tasktabbar/
├── WindowsSimpleTaskTabBar.sln    Solution file
├── Directory.Build.props          Settings shared by every project
├── global.json                    .NET SDK version pin
├── .editorconfig                  Code style and line endings
├── .gitattributes                 Line endings as stored in Git
├── .gitignore
├── LICENSE
├── CLAUDE.md                      Notes for AI coding assistants
├── README.md                      English
├── README.ja.md                   Japanese
├── .github/
│   └── workflows/
│       ├── build.yml              Build and test, on pushes and pull requests
│       └── release.yml            Build, test, package, and publish a release
├── docs/
│   ├── architecture.md            This document
│   └── architecture.ja.md         Japanese translation
├── src/
│   ├── WindowsSimpleTaskTabBar.Core/       Logic with no UI dependency
│   │   ├── Layout/
│   │   │   ├── BarMetrics.cs               Drawing sizes, from bar height and DPI
│   │   │   └── TabStrip.cs                 Tab width, overflow, scroll arithmetic
│   │   └── Settings/
│   │       └── AppSettings.cs              The settings and their defaults
│   └── WindowsSimpleTaskTabBar/            The application
│       ├── Program.cs                      Entry point
│       ├── Interop/
│       │   └── NativeMethods.cs            Windows API declarations
│       ├── Services/
│       │   ├── SettingsStore.cs            Reading and writing the settings file
│       │   └── WindowService.cs            Enumerate, activate, close windows
│       └── UI/
│           ├── MainForm.cs                 AppBar registration, painting, input
│           └── SettingsForm.cs             The settings dialog
└── tests/
    └── WindowsSimpleTaskTabBar.Tests/      Unit tests
        ├── AppSettingsTests.cs
        ├── BarMetricsTests.cs
        └── TabStripTests.cs
```

## Why src and tests are separate

This follows the common .NET convention. Keeping sources and tests in one directory becomes
hard to navigate as soon as the number of projects grows.

## Why Core is a separate project

`WindowsSimpleTaskTabBar.Core` holds only logic that depends on neither the UI nor the Windows
API. That gives two benefits:

1. **It can be tested on any operating system.** Code that calls the Windows API only runs on
   Windows, so mixing the two would restrict where tests can run.
2. **The test surface is explicit.** Calculations and rules live in Core; painting and API
   calls live in the application.

Core targets `netstandard2.0` so that both .NET 8 and .NET Framework 4.8 can consume it.

The application **compiles the Core sources directly rather than referencing the DLL**. A DLL
reference would mean shipping two files, which conflicts with the goal of distributing a single
executable. The test project references Core as a normal library.

## Multi-targeting

`src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj` declares two target frameworks:

```xml
<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>
```

One build produces both, from the same sources:

- **`net48`** targets .NET Framework 4.8, which ships with Windows 10 version 1903 and later and
  with Windows 11. Users install nothing. **This is the only build distributed.**
- **`net8.0-windows`** targets .NET 8. It is built and tested on every run but never published:
  it is a second compiler over the same source, and it keeps the door open should .NET Framework
  stop being an option.

A build carrying its own copy of .NET 8 was published for a while and has been dropped. At about
69 MB and x64 only, it helped solely on versions of Windows that are themselves out of support,
and .NET Framework 4.8 can be installed there anyway.

Differences between the two are handled with `#if NETFRAMEWORK` in `Program.cs`.

## Layering

```
Program.cs
   ↓
UI/MainForm.cs  ──→  Core/Layout/                (calculations)
   ↓
Services/WindowService.cs                       (window operations)
   ↓
Interop/NativeMethods.cs                        (Windows API)
```

Calls only go downward. `DllImport` declarations live exclusively in `Interop/NativeMethods.cs`
and must not appear anywhere else.

## Key implementation notes

### Sitting above the taskbar

`UI/MainForm.cs` registers an AppBar through `SHAppBarMessage`, which reserves part of the
desktop work area. Because the area is reserved, maximized windows do not cover the bar. Simply
setting a window to topmost would not achieve this — it would overlap other windows instead.

### Three-step activation

See the README section on reliable window activation. The three strategies are deliberate: which
one succeeds varies by environment, so none of them should be removed as redundant.

### Too many tabs to fit

Tabs share the width evenly until they reach the minimum in `Core/Layout/BarMetrics.cs`, which
is wide enough for the icon and the first four characters of the title. Below that they stop
shrinking and the row scrolls instead, by the mouse wheel, by an arrow at each end, or
automatically when the foreground window changes. No tab is ever dropped: a window missing from
the bar is the one failure this application must not have.

An icon-only stage was tried and removed. A row of windows from one application shows the same
icon over and over, so the text is the only thing that tells them apart.

### Dragging a tab

A left press on a tab does nothing on its own. The window is activated on release, and only if
the pointer is still on the same tab, because the same press may turn out to be a drag. Acting on
the press instead would minimize a window the moment the user started to drag its tab.

The drop position comes from `TabStrip.DropIndex`, a pure function: a tab takes the next slot as
soon as its leading edge passes the middle of it. `RefreshTabs` keeps running during a drag, so
the dragged tab is tracked by window handle rather than by index, and its position is reapplied
after every layout pass.

### Two menus, shown by hand

The bar does not use the `ContextMenuStrip` property. That property always shows the same menu,
and which menu belongs here depends on where the click landed: a tab has Close, Close others and
Minimize, while the space around the tabs has Settings, Refresh and Exit. The right button is
therefore read in `OnMouseUp`, and the menu shown from there. The tab menu acts on a window
handle rather than an index, so a refresh while it is open cannot move it to another window; the
commands that close one side of the row look the tab's position up when they run, for the same
reason.

### Refresh strategy

`SetWinEventHook` reports window creation, destruction, show, hide, title change, and foreground
change. The callback only sets a dirty flag; a 250 ms timer performs the actual refresh so that
bursts of events collapse into a single update. A full refresh also runs every two seconds as a
safety net in case an event is missed.
