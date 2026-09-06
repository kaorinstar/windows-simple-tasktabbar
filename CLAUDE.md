# CLAUDE.md

Guidance for AI coding assistants working in this repository.

## What this application is

A slim always-on bar above the Windows taskbar that shows every open window as a Chrome-style
tab.

**The problem it solves is that the window you want to switch to keeps ending up behind the
one you were just using.** Merging several windows into one container is deliberately out of
scope. This application only displays windows and switches between them. Do not change that
decision.

## Language policy

- **All code, comments, identifiers, commit messages, and documentation are written in
  English.** This project is published publicly.
- `README.ja.md` and `docs/architecture.ja.md` are Japanese translations. When you change
  `README.md` or `docs/architecture.md`, update the Japanese file in the same commit so the two
  stay in sync.
- User-facing strings in the application are English. There is no localization framework yet.

## Build and test

```
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
```

Warnings are treated as errors. Do not call a task finished while a warning remains.

Producing the package that is actually distributed:

```
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net48 -o artifacts
```

## Verification

Layout and activation behaviour cannot be confirmed by building alone. After changing code,
state clearly what a human needs to check by running the application. "It builds" is not the
same as "it works".

## Design rules

Full details are in `docs/architecture.md`. The three rules that matter most:

1. **All `DllImport` declarations live in `Interop/NativeMethods.cs`.** Do not add them
   elsewhere.
2. **Logic independent of the UI and the Windows API goes in
   `src/WindowsSimpleTaskTabBar.Core/`.** Code placed there is testable on any platform.
   Consider it first when adding new calculations.
3. **Core sources are compiled into the application rather than referenced as a DLL.** This
   keeps distribution to a single executable. Do not change it to a project reference.

Calls flow in one direction only:

```
Program.cs → UI/MainForm.cs → Services/WindowService.cs → Interop/NativeMethods.cs
                    ↓
          Core/Layout/TabStrip.cs, Core/Layout/BarMetrics.cs
```

## Conventions

- Line endings are LF, except `.bat`, `.cmd`, and `.ps1`, which use CRLF. This is enforced by
  `.gitattributes`.
- Branch names are `<type>/<issue number>-<short description>`, for example `fix/18-tab-layout`,
  `feat/13-tray-icon`, `ci/27-split-workflow`, `docs/20-version-file`. The types are `feat`,
  `fix`, `docs`, `ci` and `chore`. Drop the issue number when the work has no issue.
  A Claude Code session is assigned a `claude/...` branch by default. **That name is not part of
  this convention** — it describes the session rather than the change. Point it out and move the
  work to a branch that follows the convention before pushing.
- Two target frameworks are supported: `net8.0-windows` and `net48`. Differences are handled
  with `#if NETFRAMEWORK`. **Do not break the net48 target.** Running on the .NET Framework 4.8
  that ships with Windows 10 and 11 is a hard distribution requirement.

## Things that look redundant but are not

### The three-step activation in `Services/WindowService.cs`

Windows prevents a process that is not in the foreground from bringing a window forward, so a
plain `SetForegroundWindow` call fails. `Activate` tries three strategies in order:

1. `SetForegroundWindow` directly
2. `AttachThreadInput` against the foreground and target input threads, then activate
3. An Alt key press and release to lift the foreground lock, then activate

**This sequence is the core of the application.** Which strategy succeeds depends on the
environment. Do not simplify it on the grounds that it currently works.

The bar itself is intentionally allowed to activate on click — it does not set
`WS_EX_NOACTIVATE` — because a process already in the foreground is exempt from the
restriction.

### The two-second safety-net refresh

`SetWinEventHook` occasionally misses events. The periodic refresh covers that case.

## Not yet implemented

In rough priority order:

1. Grouping by application when there are many tabs
2. Multi-monitor support (currently primary monitor only)
3. Window preview on hover
4. Pinned applications
5. Built-in start-with-Windows option

## Known limitations

- Elevated applications cannot be controlled, because of Windows integrity levels. Running this
  application elevated works but means running elevated permanently.
- Full-screen applications cover the bar. This is normal AppBar behaviour.
- The order a user drags tabs into is not saved, so it is lost when the application exits.
- A tab never shrinks below its icon and the first four characters of its title. Past that the
  row scrolls, so on a narrow screen only part of the row is visible at a time.

## Continuous integration

Two workflows run on Windows, so that a verification run never needs write access to the
repository.

- `.github/workflows/build.yml` builds and tests for every push to `main` and every pull
  request. It packages nothing and runs with `contents: read`.
- `.github/workflows/release.yml` builds, tests and packages. Pushing a tag such as `v0.1.0`
  publishes a release with the net48 package attached. Starting it by hand produces the same
  package as a build artifact and creates no release. Only this workflow gets `contents: write`.

Both files carry the same build and test steps. Change them together.

The net48 package is the only one distributed: .NET Framework 4.8 ships with every supported
version of Windows, and the build is AnyCPU, so it covers ARM as well. The net8 target is still
built and tested on every run, as a second compiler over the same source.

## History

The initial design and implementation were produced in a chat session with Claude running on
Linux. The `net48` build was verified there, but the application has not been exercised on
Windows from that environment.
