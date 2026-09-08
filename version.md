# Version history

The newest version is at the top. Each entry says what changed for someone using the
application, rather than which pull requests were merged.

A release takes its notes from the section whose heading matches its tag. Entries are written as
each change lands, under `## v0.5.0

- The interface is now available in twelve languages: English, Japanese, Simplified Chinese,
  Traditional Chinese, Russian, German, French, Spanish, Portuguese (Brazil), Korean, Polish and
  Italian. It follows Windows, so a machine set to one of the twelve shows it without anything
  being set, and any other display language shows English. It can also be chosen by hand in the
  settings, and the choice is kept between runs. Changing it applies at once: the bar, its two
  menus and the settings dialog are all redrawn without a restart.
- Only English and Japanese have been read by someone who knows them. Corrections to the other
  ten languages are welcome as issues or pull requests.
- The font follows the language. Chinese, Japanese and Korean share code points, so one font
  cannot serve all three: each is drawn in a font of its own, and every other language in
  Segoe UI, which is the font Windows itself uses. English therefore moves from Yu Gothic UI,
  which the bar used for everything, to Segoe UI. A font that is missing from Windows falls back
  to the one Windows draws its own dialogs in.
- The heading of each group of settings is now visible. Bar height, Colours and Tab groups each
  had one, and the contents of the group were being drawn over it, leaving the heading blank and
  a gap at the foot of the group instead.
- The executable is 111,104 bytes, against 83,456 in v0.4.0. The twelve string tables account
  for most of the difference.

## v0.4.0

- The colours can now be chosen in the settings: follow Windows, always light, or always dark.
  Following Windows stays the default, so nothing changes until you pick one of the other two.
  With light or dark chosen, the bar keeps it when Windows switches its own setting, including
  the automatic switch some people schedule.
- Two tab groups left on the automatic colour are no longer marked in the same one. There are
  eight colours, and each group used to take the one its name gave it without looking at what the
  other groups held, so with three groups there was about one chance in three of a repeat. A
  colour chosen by hand is now reserved first, and a group left automatic moves to the next free
  colour when the one its name gives is taken. Past eight groups a repeat cannot be avoided.
- Two groups side by side on the bar are no longer marked in the same colour either. This covers
  the groups that are one application, which are not in the settings and so could not be held
  apart there: several folder windows beside several of something else came out in one colour and
  read as a single group. A colour chosen by hand is kept, and the group beside it moves instead.
  Two groups in the same colour with something between them are left as they are.
- The colour list in the settings names each colour and shows a square of it, in place of
  "Colour 1" to "Colour 8".
- The list of applications in the settings no longer jumps back to the top each time a box is
  ticked. It stays where it was scrolled to.
- The executable is 83,456 bytes, against 77,312 in v0.3.0.

## v0.3.0

- Tabs can be grouped by the application that owns them, from a new setting. Windows of one
  application sit together and share a colour along the top edge of their tabs, with a rule
  between one group and the next, and a window opened later joins its application rather than the
  end of the row. Applications can also be combined into a group of your own, with a name and a
  colour you choose, so a browser and an editor can be shown as one group. The setting is off
  until you turn it on, and with it off the row behaves exactly as it did.
- With grouping on, dragging a tab moves it inside its own group, and dragging it past a
  neighbouring group carries the whole group with it, so the row can still be put in any order
  you like. A window that is the only one of its application travels on its own.
- The executable is 77,312 bytes, against 60,416 in v0.2.0.

## v0.2.0

- Closing the bar now releases everything it holds, whichever way it is closed. The tray icon
  disappears at once and nothing is left behind in the notification area.
- The bar follows the Windows light and dark setting as soon as it changes, including the
  automatic switch some people schedule. It used to keep the colours it read when it started,
  until it was restarted.
- The executable is 60,416 bytes, against 59,904 in v0.1.0.

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
