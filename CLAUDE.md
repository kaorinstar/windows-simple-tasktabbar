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

1. Multi-monitor support (currently primary monitor only)
2. Window preview on hover
3. Pinned applications
4. Built-in start-with-Windows option

## Known limitations

- Elevated applications cannot be controlled, because of Windows integrity levels. Running this
  application elevated works but means running elevated permanently.
- Full-screen applications cover the bar. This is normal AppBar behaviour.
- The order a user drags tabs into is not saved, so it is lost when the application exits.
  Turning grouping off also leaves the tabs where grouping put them: the order they opened in is
  not recorded anywhere.
- Groups cannot be reordered, and two applications whose executables share a file name are
  treated as one application.
- A tab never shrinks below its icon and the first four characters of its title. Past that the
  row scrolls, so on a narrow screen only part of the row is visible at a time.

## Continuous integration

Two workflows build on Windows, so that a verification run never needs write access to the
repository.

- `.github/workflows/build.yml` builds and tests for every push to `main` and every pull
  request. It packages nothing and runs with `contents: read`.
- `.github/workflows/release.yml` builds, tests and packages. Pushing a tag such as `v0.1.0`
  publishes a release with the net48 package attached. Starting it by hand produces the same
  package as a build artifact and creates no release. Only this workflow gets `contents: write`.

A third file, `.github/workflows/report-build-status.yml`, is called by both once their build job
finishes, and only for pushes. On a failure it opens an issue labelled `ci-failure`, or comments on
the one already open rather than opening a second; on the next success it comments on that issue
too. It never closes it: a green build shows the symptom is gone, not that the cause was
understood. Pull requests are excluded on purpose, since a failing pull request already shows the
failure on itself. `issues: write` is granted to the calling job alone, so the build keeps its
read-only token. Nothing is built there, so it runs on Linux.

Both files carry the same build and test steps. Change them together. The one deliberate
difference is the version: a release build takes it from the tag, while a verification build keeps
the value in `Directory.Build.props`.

Both check the dependencies for known vulnerabilities before building, with
`dotnet list package --vulnerable --include-transitive`. That command exits 0 whether or not it
finds anything, so the step reads its output and fails explicitly. Do not replace it with a NuGet
package: the check comes with the SDK. It covers other people's code only; analysing this
project's own code needs CodeQL, which is free on public repositories alone (#26).

Every action is pinned to a full commit SHA, with its version in a comment beside it. A tag is a
pointer its owner can move, and `softprops/action-gh-release` is a third-party action that runs
with write access. `.github/dependabot.yml` watches NuGet and the actions weekly, so pinning does
not mean going stale, and its pull requests run `build.yml` like any other. Dependabot names its
own branches (`dependabot/...`), which is outside the naming convention above and cannot be
changed.

The net48 package is the only one distributed: .NET Framework 4.8 ships with every supported
version of Windows, and the build is AnyCPU, so it covers ARM as well. The net8 target is still
built and tested on every run, as a second compiler over the same source.

## Releasing

Tags are `vMAJOR.MINOR.PATCH`, as semantic versioning describes, for example `v0.1.0`. Below
`1.0.0` the minor number covers additions and changes, and the patch number covers fixes alone. No
leading zeros: the tag, minus its leading `v`, is passed to the build as `-p:Version`, and every
part of `AssemblyVersion` must be a plain integer, which `01` is not. A published executable
therefore reports the version of its release in its file properties. Every other build keeps the
`<Version>` in `Directory.Build.props`.

Date-based tags (`vYYYY.M.D`) were used while #20 and #22 were written, and dropped before the
first release. GitHub already prints the date of every release, so a date in the number added
little, and it could say neither how large a change was nor that the application is still before
`1.0.0`. **Do not reintroduce them.** Windows compares versions numerically, so a release published
as `2026.9.6` could never be followed by `1.0.0`.

`version.md` is the changelog, newest version at the top, one section per version.
`version.ja.md` is its Japanese translation and is updated in the same commit, as the language
policy requires. `.github/workflows/release.yml` reads the section whose heading matches the tag
and uses it as the release notes, so the entry has to be committed **before** the tag is pushed. A
malformed tag, a missing section or an empty one fails the workflow before anything is built.

Each entry in `version.md` quotes the size of that release's executable. Measure it from the
published file rather than copying the entry above it: the figure sat at 23 KB while the
application grew to more than twice that, and it reached a published release that way. The
READMEs say only "under 100 KB" and point at `version.md`, so there is one number to keep right
rather than five.

## History

The initial design and implementation were produced in a chat session with Claude running on
Linux. The `net48` build was verified there, but the application has not been exercised on
Windows from that environment.
