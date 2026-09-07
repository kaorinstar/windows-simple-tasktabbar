# Version history

The newest version is at the top. Each entry says what changed for someone using the
application, rather than which pull requests were merged.

A release takes its notes from the section whose heading matches its tag. Entries are written as
each change lands, under `## Unreleased`, and that heading is renamed to the version number when
the release is prepared. Nothing matches `Unreleased`, so a tag pushed while entries are still
sitting there fails the release workflow rather than publishing an empty release. See "Releasing"
in [README.md](README.md).

`version.ja.md` is the Japanese translation of this file and is updated in the same commit.

## v0.2.0

- Closing the bar now releases everything it holds, whichever way it is closed. The tray icon
  disappears at once and nothing is left behind in the notification area.
- The bar follows the Windows light and dark setting as soon as it changes, including the
  automatic switch some people schedule. It used to keep the colours it read when it started,
  until it was restarted.

## v0.1.0

The first release.

- A slim bar sits above the Windows taskbar and shows every open window as a tab. Clicking a tab
  brings that window to the front, restoring it if it was minimized. Clicking the active tab
  minimizes it, as the Windows taskbar does.
- Windows restricts which processes may bring a window forward. Three activation strategies are
  tried in turn, so a click switches windows in cases where a plain request would be refused.
- A tab closes from its × or with a middle click, and can be dragged sideways to another position.
- A right click on a tab opens a menu with Close, Close other tabs, Close tabs to the left, Close
  tabs to the right and Minimize. A right click on the space around the tabs opens a menu with
  Settings, Refresh and Exit.
- A tray icon carries the same menu, so the application can still be reached when the bar is
  covered.
- Hovering a tab shows the full window title.
- The bar height can be set to standard (34 px) or compact (24 px) in the settings dialog. The
  choice takes effect straight away and is kept in `%APPDATA%\WindowsSimpleTaskTabBar\`.
- When there are more tabs than fit, the row scrolls with the mouse wheel or the arrows at either
  end, and follows the window you switch to. No window is dropped from the row.
- The bar follows the Windows light or dark colour setting, read when it starts.
- One executable of 59,904 bytes, with nothing to install alongside it. It is built against
  .NET Framework 4.8, which ships with Windows 10 version 1903 and later and with Windows 11, and
  it is AnyCPU, so it also runs on ARM versions of Windows.
