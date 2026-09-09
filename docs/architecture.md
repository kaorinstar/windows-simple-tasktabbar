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
├── version.md                     Changelog, and the source of the release notes
├── version.ja.md                  Japanese translation
├── .github/
│   └── workflows/
│       ├── build.yml              Build and test, on pushes and pull requests
│       ├── release.yml            Build, test, package, and publish a release
│       └── report-build-status.yml  Open an issue when a push fails to build
├── docs/
│   ├── architecture.md            This document
│   └── architecture.ja.md         Japanese translation
├── src/
│   ├── WindowsSimpleTaskTabBar.Core/       Logic with no UI dependency
│   │   ├── Focus/
│   │   │   └── ActiveMark.cs               Which tab is marked as the window in front
│   │   ├── Grouping/
│   │   │   └── TabGrouping.cs              Which group a tab is in, and the row's order
│   │   ├── Layout/
│   │   │   ├── BarMetrics.cs               Drawing sizes, from bar height and DPI
│   │   │   └── TabStrip.cs                 Tab width, overflow, scroll arithmetic
│   │   ├── Localization/
│   │   │   ├── LanguageInfo.cs             One language: its name and its font
│   │   │   ├── Languages.cs                The languages offered, and which one to use
│   │   │   ├── StringId.cs                 The name of every piece of interface text
│   │   │   ├── UiStrings.cs                What each one says, in each language
│   │   │   └── UiText.cs                   The text in one language, as callers read it
│   │   ├── Settings/
│   │   │   ├── AppGroup.cs                 One group the user defined by hand
│   │   │   └── AppSettings.cs              The settings and their defaults
│   │   └── Theme/
│   │       └── BarPalette.cs               The colours to draw with, from the setting
│   └── WindowsSimpleTaskTabBar/            The application
│       ├── Program.cs                      Entry point
│       ├── Interop/
│       │   └── NativeMethods.cs            Windows API declarations
│       ├── Services/
│       │   ├── ProcessInfoCache.cs         The executable behind each window, remembered
│       │   ├── SettingsStore.cs            Reading and writing the settings file
│       │   └── WindowService.cs            Enumerate, activate, close windows
│       └── UI/
│           ├── AccentPalette.cs            The square of colour shown beside each accent
│           ├── MainForm.cs                 AppBar registration, painting, input
│           ├── SettingsForm.cs             The settings dialog
│           └── UiFonts.cs                  The font of the active language, with a fallback
└── tests/
    └── WindowsSimpleTaskTabBar.Tests/      Unit tests
        ├── ActiveMarkTests.cs
        ├── AppSettingsTests.cs
        ├── BarMetricsTests.cs
        ├── BarPaletteTests.cs
        ├── LocalizationTests.cs
        ├── TabGroupingTests.cs
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
   │             ──→  Core/Grouping/              (which tab is in which group)
   │             ──→  Core/Focus/                 (which tab is marked as in front)
   │             ──→  Core/Localization/          (what every piece of text says)
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

### Marking the active tab

The tab of the window in the foreground is drawn with an outline and is otherwise exactly the same
tab as every other one - the same bounds, the same size, the same place for its icon and its title.

Colour could not carry it alone. The active fill against an inactive tab is 1.27 to 1 on the light
palette and 1.63 to 1 on the dark one, where WCAG asks for 3 to 1 to tell one part of an interface
from another, and reaching that by fill alone would mean a mid grey tab in a light bar - every
other tab paying for the one being marked. `BarPalette.TabActiveOutline` carries it instead, and
`BarPaletteTests` holds it to 3 to 1 against both the fill it surrounds and the bar behind it.

Drawing the active tab taller was tried first and removed. A group's accent runs along the top edge
of its tabs, and `DrawGroupBand` puts it at the tab's own top so that the accent joins up across
the row. Raising one tab moved its accent down from the edge it shares with its neighbours and
squared off the ends the corner radius had rounded, so the band came out of line exactly where a
group most needs to read as one. Height and the accent cannot both own the top edge.

The outline is drawn before the accent, so a marked tab keeps the full thickness of its band and
the outline marks it down the sides. Its foot goes one outline width past the bottom of the bar:
the outline follows a closed path, and a bottom edge left on the last row of pixels would be drawn
as a line under the tab rather than the open foot a tab standing on the edge of the bar should
have.

### Which tab the mark goes on

Not simply the foreground window. The bar activates itself when it is clicked, as the section on
three-step activation explains, so touching the bar at all takes the foreground away from the
window the user was in. A click hands it back on release, through `ClickTab`. A drag does not: it
ends with no window to activate, so the bar is still in front when the button comes up, and the
row would sit unmarked until the user went somewhere else. The fill alone made that easy to miss;
an outline does not.

So `WindowToMark` asks `Core/Focus/ActiveMark.cs` instead, and a window of this application's own
never takes the mark - the window that held it keeps it, which is what the Windows taskbar does
with its own highlight while it is being used. A window the bar does not list cannot hold the mark
either, so a handle Windows has since given to something else cannot inherit one.

`ActiveMark` is given three booleans rather than window handles, so the rule can be tested on any
platform while asking Windows which window is which stays in the UI layer. `IsOwnWindow` compares
process ids rather than handles: the bar is not the only window this application puts on screen,
and the settings dialog or a menu can be what a click leaves in the foreground.

This also settles a piece of timing. `ClickTab` minimizes a tab that is already in front, and it
reads `tab.Active` to decide. That flag used to depend on whether the 250 ms refresh happened to
land between the press and the release; now the mark stays put while the bar is in front, so the
answer is the same either way.

Giving the active tab a larger share of the width was considered and dropped as well (#9).
`TabStrip.Measure` returns one width for the whole row and four calculations read it, so two
widths would mean rebuilding all four. The active tab also changes whenever the user switches
window, so its width would change with it and move every tab to its right - the opposite of what
a bar for reaching a window at once should do.

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

### The interface text

Nothing the user reads is written where it is drawn. Every piece of it has a name in
`Core/Localization/StringId.cs` and a line in each table in `Core/Localization/UiStrings.cs`,
and `UiText` reads one language of that table. English is the source language; every other table
is a translation of it.

**A plain table rather than `.resx` and satellite assemblies.** A satellite assembly adds a
folder and a DLL for each language, and this application is distributed as a single executable.
The tables are compiled in with everything else in Core, which also means they can be tested on
any operating system. Two unit tests hold them together: every table answers every name, and no
table holds a name the others do not.

**Adding a language costs one table and one row.** The table goes in `UiStrings`, the row in
`Languages.All`. The settings dialog lists whatever stands in that list, under each language's
own name, so nothing else is edited. `Languages.Canonical` says which row a Windows culture
belongs to: Chinese and Portuguese cannot be answered by the two-letter code alone, and
everything else is matched on it.

**Only English and Japanese have been read by someone who knows them.** The parity test holds
every table to the same set of names; nothing holds a translation to what it ought to say. A
translation that reads wrongly to a native speaker is worth an issue or a pull request.

**The font follows the language.** Chinese, Japanese and Korean share code points, so one font
cannot serve all three: a Japanese font draws Chinese text with Japanese letter shapes, which a
Chinese reader sees as wrong rather than as a missing character. Each language names its family
in `Languages.All`, and `UI/UiFonts.cs` falls back to the font Windows draws its own dialogs in
when the family is not installed, which a stripped-down Windows can be missing.

**A change of language applies at once**, like every other setting here. The bar rebuilds its
menus and its font, and the settings dialog builds itself again from the same code that built it
the first time, so there is no second list of which control holds which piece of text. Two
things wait for the message that changed them to finish being handled: the dialog rebuilds
itself through `BeginInvoke`, because it disposes the box the change came from, and the menus
the bar replaces are kept until it closes, because the menu whose Settings item opened the
dialog is still held by Windows Forms further up the stack.

### Grouping the row by application

`Core/Grouping/TabGrouping.cs` answers two questions that need neither the UI nor the Windows
API: which group a tab belongs to, and what order the row is drawn in. The bar hands it one
group name per tab and gets back the order, so the rule is unit tested rather than inferred from
what the bar looks like.

`Arrange` has to give an arranged row back unchanged. `RefreshTabs` runs four times a second, so
anything that moved a tab on the second pass would move it again on the third and the row would
never come to rest. A group takes the place of its first window and the windows inside it keep
the order they arrived in, which is what makes that true.

It is `_order` that gets arranged, not `_tabs`. `_order` is the display order that survives the
next refresh, and dragging writes the same move to both lists by index, so arranging one alone
would let the two disagree. Turning grouping off therefore leaves the tabs where grouping put
them: the order they opened in is not recorded anywhere.

**A drag moves one tab inside its group, and the whole group once it passes beyond it.**
`TabGrouping.PlanDrag` decides which, and `TabStrip.MoveRange` applies it to the three lists that
have to agree: `_order`, `_tabs` and the group ids the next drag step reads.

A single tab cannot leave its group, because grouping is worked out again on the next refresh and
there is nowhere to record that it had left: within 250 ms it would be back, and a bar that undoes
what the user just did is worse than one that would not let them do it. A whole group has no such
problem, and the reason is the rule `Arrange` already follows. Arrange orders groups by where each
one's first window sits, so a group whose tabs move together as a block is already the order
Arrange would give back. The move is idempotent, and it stands. `ArrangeAfterAGroupMoveChangesNothing`
in the tests is what holds that: if it ever fails, a dragged group springs back to where it was.

A window that is the only one of its application, or one whose executable could not be read, is a
block of one, so it travels alone.

**A drag that has moved a group keeps moving that group and nothing else.** A group move leaves
the pointer over the group it has just carried across, and reading that as a move inside the group
would pull the held tab to whichever slot the pointer had reached, reordering tabs the user never
took hold of - dragging a group somewhere quietly rearranged its contents. `PlanDrag` is told
whether the drag is already carrying a group and answers with nothing rather than a move inside
one. Reordering inside a group is what a drag that never leaves it is for.

**Anything that crosses a group waits for the pointer to travel as far as the row shifted last
time**, which `TabStrip.MovedFarEnough` decides. This is not a nicety, and it took two attempts.

`DropIndex` reads the pointer alone, so the two arrangements - this group before that one, or
after it - are separated by a single pixel of pointer travel. Worse than a shaking hand: a tab
that has just jumped a group is, from its new place, being asked to jump back, and it does so on
every mouse move without the pointer going anywhere at all. That was seen as a single tab
flickering left and right over a group.

The measure is how far the row shifted, not how many tabs moved. A single tab passing a group of
two shifts the row by two just as a group of two does, and the first attempt at this - which
asked for room only when more than one tab moved, and only ever a single tab's width - left both
of those cases short. Undoing a jump of two tabs now asks for two tabs of travel back, the same
distance that made it. A shift of one slot is the ordinary swap with a neighbour, which needs no
help: the tab itself takes the slot under the pointer, leaving half a tab of room. A tab dragged
inside its own group is not held back either, having the room already.

**The layout is not changed at all.** `TabStrip.Measure` gives every tab one width and one gap,
and `DropIndex`, `ScrollToShow` and the hit testing all read that same step; a wider gap at a
group boundary would mean changing all of them together, and the position a tab is drawn at
could no longer be assumed to be the position it would be dropped at. So a group is marked with
an accent along the top edge of its tabs, carried across the gap between two tabs of one group
so the group reads as a single band, and the rule between two groups is drawn inside that same
gap. If a wider separation is ever wanted, it needs a second measuring function and every reader
of the step moved onto it, which is a change of its own.

A group of one window is not marked. The accent says "these belong together", which a single tab
has nothing to say to, and marking every tab of a row where no two windows share an application
would colour the whole bar and tell the user nothing.

### Which accent a group is given

`TabGrouping.AccentFor` answers with a number from 0 to 7, which is an index into
`BarPalette.Accents` and so into the shade of the palette in use. A group whose accent the user
chose keeps that one. The rest are derived, and the derivation has two parts.

The first is a hash of the group's name, FNV-1a over its lower-cased characters, taken modulo the
palette size. It is written out rather than taken from `string.GetHashCode`, which is randomized
per process on .NET Core and later: an application would be a different colour every time the bar
started, and a different colour again on the other target framework.

The second part is there because a hash cannot avoid a collision. Eight accents are few enough
that two different names agreeing is met rather than unlucky - about one chance in three with
three groups, and certain past eight - and two groups in the same colour is exactly what the
accent is meant to rule out. So the accents chosen by hand are reserved first, and then the groups
left automatic are walked in the order the settings hold them, each keeping the accent its name
gives when that one is still free and taking the next free one when it is not.

The settings order is used because it is the one order available that does not change by itself,
which is what keeps the answer the same on every run. The cost is that adding a group can move the
accent of a group listed after it; choosing an accent by hand is how a user holds one still.

Only the groups the user defined take part in that. A group that is one application is not in the
settings at all, so there is nothing there to hold it apart from anything else, and a row of half
a dozen applications repeats an accent often.

`TabGrouping.AccentsFor` covers what is left, on the row rather than in the settings. Where a
repeat does harm is between neighbours: the band is carried across the gap inside a group, so two
groups side by side in one colour read as a single group, which is the one thing the band is there
to say. Two groups in the same colour with something between them are only two groups in the same
colour. So the row is walked from the front, and where a group's accent matches the group before
it, one of the two moves on. The one that moves is the one whose accent was derived; an accent the
user chose stays where they put it and its neighbour gives way, and when both were chosen both are
left alone. A group of one window is not marked, and an unmarked group breaks the band, so the
group after it has nothing to differ from.

This is the row's own order, so a group can change colour when it is dragged to a new neighbour or
when a window opens beside it. That is what the guarantee costs, and it is paid on the two groups
the user is looking at rather than across the whole bar. Ordering every group by the row instead,
and holding all of them apart that way, would spread the same instability over every colour on the
bar.

### Reading the process behind a window

`WindowService.GetExecutablePath` opens the process with `PROCESS_QUERY_LIMITED_INFORMATION` and
asks `QueryFullProcessImageNameW`. `Process.MainModule.FileName` would be shorter, but it needs
`PROCESS_VM_READ`, which is refused for an elevated process and for one of a different bitness,
and it answers with an exception rather than a result. The limited right is granted in both
cases, so a window owned by an elevated application still lands in the right group even though the
bar cannot fully control it.

A packaged application - Calculator, Settings, Photos - is drawn in an `ApplicationFrameWindow`
owned by `ApplicationFrameHost.exe`. Asked directly, every one of them answers with the same
executable and they would all be shown as one group, so the frame's children are asked instead.

`ProcessInfoCache` remembers the answers, including the failures: `RefreshTabs` runs four times a
second, and a window this application may not query would otherwise be asked again on every
pass. Nothing expires, because a window cannot change the process that owns it. Entries are
dropped from the same live set the icon cache is pruned against, so a handle Windows later reuses
is looked up again rather than answered from the old entry.

Grouping is matched on the executable's file name rather than its full path, because that is how
people recognise an application; two copies of one program installed in different folders are
the same application to the person looking at the bar. The cost is that two unrelated programs
both called `app.exe` are treated as one.

### Who owns what, and how a leak is caught

The bar is open for as long as the user is logged in, so anything it fails to release stays lost
for the whole session. Ownership is therefore written down rather than assumed.

| Object | Owner | Released |
|---|---|---|
| Window icons | `_iconCache` in `MainForm` | when the window closes, when the icon is refreshed, and in `ReleaseResources` |
| `Font`, `ToolTip`, both menus, `NotifyIcon`, the timer | `MainForm` | `ReleaseResources` |
| The tray icon handle from `ExtractIconExW` | `MainForm` | `DestroyIcon` in `ReleaseResources` |
| Event hooks from `SetWinEventHook` | `_hooks` in `MainForm` | `UnhookWinEvent` in `ReleaseResources` |
| The AppBar registration | `MainForm` | `UnregisterAppBar` in `ReleaseResources` |
| The process handle from `OpenProcess` | `WindowService.GetExecutablePath` | `CloseHandle` in that method's `finally` |
| `Pen`, `SolidBrush`, `GraphicsPath` while painting | the `using` statement around them | end of the statement |
| Controls in `SettingsForm` | the `Controls` collection they are added to | the form's own `Dispose` |

`ReleaseResources` runs from two places and does its work only once: `OnFormClosing`, so the
desktop gets its space back the moment the bar is closed, and `Dispose(bool)`, so nothing is left
registered when the form is disposed without having been closed. It runs before the base
`Dispose`, because removing the AppBar registration needs a window handle that still exists.

Three analyzer rules guard this, raised to warnings in `.editorconfig` and therefore build
failures: `CA1001` for a type that holds a disposable field without being disposable itself,
`CA2000` for an object created and then dropped, and `CA2213` for a field `Dispose` never
releases. `EnableNETAnalyzers` in `Directory.Build.props` is what brings them to the `net48`
target, which the SDK would otherwise leave unanalysed.

Where a Windows Forms container owns a field — a control in a `Controls` collection, an item in
a menu — the field carries a `SuppressMessage` naming that owner. `CA2213` cannot see ownership
of that kind and would otherwise ask for a second release, which is one more thing to keep in
step. `CA2000` is given `dispose_ownership_transfer_at_method_call` for the same reason: without
it, every control added to a collection reads as an object dropped without being disposed.

The analyzers read the source, not a running program, so they cannot see a collection that grows
without limit. That needs a person:

1. Start the bar and open Task Manager, Details tab.
2. Add the Memory, Handles, USER objects and GDI objects columns, and find
   `WindowsSimpleTaskTabBar.exe`.
3. Open and close windows, change the bar height, drag tabs and open the menus, for at least 30
   minutes.
4. All four numbers should move up and down and settle. A number that only ever rises is the
   symptom to report.

### Refresh strategy

`SetWinEventHook` reports window creation, destruction, show, hide, title change, and foreground
change. The callback only sets a dirty flag; a 250 ms timer performs the actual refresh so that
bursts of events collapse into a single update. A full refresh also runs every two seconds as a
safety net in case an event is missed.
