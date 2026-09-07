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

## Asking the user to do something

This application only runs on Windows, and the environment an assistant works in usually cannot
run it. Anything that needs a real machine is handed to the user, so writing that hand-off is
part of the work rather than an afterthought.

Give them this, in this order, and nothing else:

1. **What to do**, numbered in the order it is performed.
2. **The exact URL** for anything on GitHub. Never "from the Actions tab" or "in the settings":
   paste the link.
3. **Commands that can be pasted as they are**, with the branch name and the paths already
   filled in. Not a template with a placeholder left in it.
4. **What to look at**, phrased so the answer is yes or no.
5. **What to send back** when the answer is no.

Leave out the design, the reasoning, and anything already reported. If the reasoning matters it
belongs in the pull request, not in the instruction. A person who has to scroll past an
explanation to find the command has been given a worse instruction, not a fuller one.

### Handing over a build to test

Two routes. Give whichever fits, with the branch name already substituted, and do not make the
reader choose between them without saying which one you mean.

**From GitHub, when there is no .NET SDK on the Windows machine**

1. Open https://github.com/kaorinstar/windows-simple-tasktabbar/actions/workflows/release.yml
2. **Run workflow** → Branch: the branch to test → **Run workflow**. There are no other inputs.
3. When the run finishes, open it and download the artifact named `WindowsSimpleTaskTabBar`
   from the Artifacts section at the bottom of the page.
4. Unzip it. `WindowsSimpleTaskTabBar.exe` is the net48 build, which is the one that ships.

A manual run publishes nothing. It stamps the version from `Directory.Build.props` and creates
no release; only pushing a `v*` tag does that.

**On a Windows machine with the .NET 8 SDK**

```
git fetch origin <branch>
git checkout <branch>
dotnet publish src/WindowsSimpleTaskTabBar/WindowsSimpleTaskTabBar.csproj -c Release -f net48 -o artifacts
```

The executable is `artifacts\WindowsSimpleTaskTabBar.exe`.

**`build.yml` is not a route to a build.** It compiles and tests and then packages nothing, so
there is no artifact on it to download. Do not send anyone to a `build.yml` run for a file.

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

The list of planned work lives in the issue tracker, not here, so that there is one copy of it to
keep current. Take priorities from there.

Two tracking issues hold most of it, each with its sub-issues in priority order: #1 for the tab
strip and #2 for the settings. #17, a bar on the left or right edge, is marked low priority and is
not scheduled. What sits under neither tracking issue is listed on its own: #62 multi-monitor
support, #21 the installer and the portable package, #35 the translated interface, #26 whether to
publish the repository.

**Do not write the list out again, here or in the roadmap sections of `README.md` and
`README.ja.md`.** It stood in all three files at once, beside the issues that already tracked it,
and had drifted from them by the time it was replaced with this pointer (#23): one item had been
absorbed into another issue and another had no issue at all. Planned work becomes an issue, and
finishing it then means closing that issue rather than editing three files. Keep the same rule for
the known limitations below: the entry stays, and it links to its issue instead of describing the
plan.

## Known limitations

- A window owned by an elevated application cannot be fully controlled, because of Windows
  integrity levels. `SetForegroundWindow` still tends to succeed, since `Activate` is called with
  the bar already in the foreground, so switching to a window that is on screen works. The
  `ShowWindow(SW_RESTORE)` that `Activate` issues first is refused, so a minimized window stays
  minimized, and closing is refused too. Neither returns an error a user would see. Task Manager
  is the example most people meet, because it elevates itself on an administrator account.
  Running this application elevated removes the limit but means running elevated permanently.
- Only the primary monitor carries a bar, and it lists every window in the session, wherever it
  is (#62).
- Full-screen applications cover the bar. This is normal AppBar behaviour.
- The order a user drags tabs into is not saved, so it is lost when the application exits.
  Turning grouping off also leaves the tabs where grouping put them: the order they opened in is
  not recorded anywhere.
- Two applications whose executables share a file name are treated as one application.
- The check for a new release finds nothing while the repository is private (#26). The failure
  looks the same as being offline, so no notice is ever shown and nothing reports the reason.
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

**Every change that a user would notice adds its entry to `## Unreleased` in the same pull request
that makes the change**, in both files. Preparing a release is then renaming that heading to the
version number rather than reconstructing the list from the commit log afterwards. No tag matches
`Unreleased`, so forgetting to rename it fails the workflow instead of publishing an empty
release.

**The same pull request sets the `<Version>` in `Directory.Build.props` to the number being
released.** Nothing fails when this one is missed, which is the difficulty with it: the release
itself takes its number from the tag and is correct either way, and only local builds, branch
builds and manual runs of `release.yml` carry the stale value into the file properties of the
executable. That is how it sat at `0.1.0` until after v0.3.0 had been published (#64). Rename the
heading and set the version together.

Each entry in `version.md` quotes the size of that release's executable. Take it from the "Show
the size of the net48 build" step of `build.yml`, which prints it on every run, rather than
copying the entry above it: the figure sat at 23 KB while the application grew to more than twice
that, and it reached a published release that way. The READMEs say only "under 100 KB" and point
at `version.md`, so there is one number to keep right rather than five.

That step is the one thing `build.yml` does that `release.yml` does not. It is there because the
number is needed while a release is being prepared, which happens in a pull request, and pull
requests run `build.yml`.

### An assistant cannot push the tag

The credentials a Claude Code session is given push branches, not tags: `git push origin v0.3.0`
comes back `HTTP 403` from GitHub, and there is no tag or release call among the GitHub tools
either. **Do not hand anyone `git tag` and `git push` commands to run on their own machine.** They
are working from a browser, and asking them to find a clone and a terminal is asking them to do
the awkward part of the job by hand.

Everything up to the tag is an assistant's to do: the changelog heading renamed to the version,
the `<Version>` in `Directory.Build.props` set to that same number, the size read from the build,
the pull request, and its merge. Then hand over exactly this:

1. Open https://github.com/kaorinstar/windows-simple-tasktabbar/releases/new
2. **Choose a tag** → type the version, for example `v0.3.0` → pick
   **Create new tag: v0.3.0 on publish**
3. Check **Target** is `main`
4. Leave the title and the description empty. `release.yml` fills them from `version.md`
5. **Publish release**

Publishing creates the tag, the tag runs `release.yml`, and the workflow attaches the net48
executable and replaces the description with the matching section of `version.md`. Watch it at
https://github.com/kaorinstar/windows-simple-tasktabbar/actions/workflows/release.yml and check
the result at https://github.com/kaorinstar/windows-simple-tasktabbar/releases: the executable
attached, the notes from `version.md`, and the size in those notes matching the size of the file
beside them.

## History

The initial design and implementation were produced in a chat session with Claude running on
Linux. The `net48` build was verified there, but the application has not been exercised on
Windows from that environment.
