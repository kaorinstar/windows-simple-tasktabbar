# Version history

The newest version is at the top. Each entry says what changed for someone using the
application, rather than which pull requests were merged.

A release takes its notes from the section whose heading matches its tag. Entries are written as
each change lands, under `## Unreleased`, and that heading is renamed to the version number when
the release is prepared. Nothing matches `Unreleased`, so a tag pushed while entries are still
sitting there fails the release workflow rather than publishing an empty release. See "Releasing"
in [README.md](README.md).

`version.ja.md` is the Japanese translation of this file and is updated in the same commit.

## Unreleased

- Applications can now be put in an order of your own. A tab used to land wherever its window
  happened to open, and dragging it somewhere else was lost when you exited. Name the
  applications you want nearest the left end under **Tab order** in the settings, in the order
  you want them there, and each new window takes its place in the row rather than the end of it.
- An application the order does not name keeps its tab at the right end, which is what every tab
  did before, so leaving this setting alone changes nothing.
- Changing the order rearranges the tabs you are looking at straight away, rather than waiting
  for the next window to open. After that the order decides where a new tab goes and nothing
  else: a tab you drag somewhere stays where you put it.
- With tabs grouped by application, the same list decides both which group comes first and the
  order of the tabs inside one.
- An application can now be kept off the bar. A background utility or a chat client you never
  switch to with the bar still took a tab, and there was no way to stop it. Tick anything you
  want left out under **Excluded applications** in the settings, or right-click one of its tabs
  and choose **Exclude this application**, which does the same in one step.
- Nothing is closed by excluding an application. Its windows stay open and only their tabs go,
  and they are back on the bar the moment you untick it.
- An application that is not running can be excluded by typing its name, so a program that
  interrupted you does not have to be started again to be kept off the bar. An excluded
  application is listed in the settings whether or not it is running, so the choice can always
  be undone.
- The shell's own windows - the taskbar, the desktop - are still kept off the bar by a list that
  is built in and cannot be shortened, so nothing here can put them on it.
- The settings window is now laid out in two columns, sorted so that the two are of a height. It
  has always taken whatever height its contents came to, which keeps it fitting its own text at
  any size and in any language, and with the boxes above added that came to more than the height
  of a screen: its own Close button went out of reach.
  Two columns halve the height and use room that was empty at the side, so the whole of it can be
  seen at once again. On a screen too short even for that, the boxes scroll and the Close button
  stays where it is.
- The note under Window preview no longer runs off the side of the settings window. It was the
  one note in the dialog that was never wrapped, and being the longest sentence in it, it decided
  how wide the window was: every box sat well short of the right-hand edge.
- The box that holds the update setting has its heading back. Its contents were placed over the
  heading rather than below it, hiding the word Updates and leaving an empty strip along the
  bottom of the box instead. Every other box in the dialog was already laid out the right way.

## v0.6.0

- The mark on the tab of the window you are using no longer comes off when you use the bar
  itself. Dragging a tab to a new place left no tab marked at all, because the bar takes the
  foreground from your window when you click it and a drag has no window to hand it back to. The
  mark now stays where it was for as long as the bar is the thing in front, which is what the
  Windows taskbar does with its own highlight.
- The tab of the window you are using is easier to pick out. It is now drawn with an outline, so
  it can be found by its edge instead of by reading titles. Its colour alone was close to the tabs
  beside it, particularly in the light theme. Nothing else changes: every tab is the same size and
  in the same place as before, a tab group keeps its unbroken colour along the top of the row, and
  dragging and scrolling behave as they did.
- Resting the pointer on a tab can now show a live picture of that window, with its full title
  underneath, which is the quickest way to tell apart several windows of one application whose
  tabs look alike. **It is off until you turn it on**, under Window preview in the settings. The
  tooltip does not appear alongside it, because the preview already says the title. The title
  carries the window's icon in front of it, as the taskbar's does.
- A minimized window gets a preview too, and it shows the window itself, not a stand-in. A window
  Windows has no picture of at all is shown as its icon and its title.
- The preview can be pointed at. Resting on a title that was too long to fit shows the whole of
  it, so a name cut short in the middle is still readable.
- The bar now looks for a newer release when it starts, and tells you when there is one: a
  notification names the version, and the menu gains an entry that opens the release page in your
  browser. Nothing is downloaded and nothing is replaced - you download the new executable
  yourself, as you did to install it. The check reads one version number from github.com, sends
  nothing about you, and runs at most once a day. Each release is announced once rather than at
  every logon, though the menu entry keeps naming it for as long as you are running an older
  version, whether or not the day's check has run. A new box in the settings dialog turns it off,
  and the menu entry still lets you ask whenever you like.
- The executable is 146,432 bytes, against 111,104 in v0.5.0.

## v0.5.0

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
