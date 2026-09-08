# Security policy

[日本語版はこちら / Japanese version](SECURITY.ja.md)

## Reporting a vulnerability

Report it privately, through GitHub's own form:

https://github.com/kaorinstar/windows-simple-tasktabbar/security/advisories/new

Please do not open an ordinary issue for one. An issue is public from the moment it is written,
and it cannot be taken back.

A report is most useful when it says what the problem is, what someone gains from it, and the
steps that reproduce it. The version of Windows and the version of the application both help; the
application's version is in the file properties of `WindowsSimpleTaskTabBar.exe`.

One person maintains this project, so a reply is not immediate. Expect one within seven days. If
none has arrived by then the report has been missed rather than turned down, so comment on the
advisory to raise it again.

## Which versions are supported

The newest release, and no other. The application is one executable with nothing installed beside
it, so a fix reaches a user by replacing that file rather than by patching a release already out.
The releases are listed at https://github.com/kaorinstar/windows-simple-tasktabbar/releases.

## What the application does

This is the ground a report is judged against, so it is written out here rather than left to be
read from the source.

- It runs as a normal user process. It asks for no elevation, installs no service, and creates no
  scheduled task.
- **It makes no network connection of any kind.** Nothing is sent anywhere and nothing is
  downloaded, so there is no telemetry, no update check and no account.
- It reads the list of open windows, their titles, and the name of the executable behind each one,
  through the Windows API.
- It writes one file: `settings.json` under `%APPDATA%\WindowsSimpleTaskTabBar\`. That file holds
  the bar height, the colour and language settings, and any tab groups defined in the settings.
- It reads one registry value, `AppsUseLightTheme` under
  `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, to follow the
  Windows light and dark setting. Nothing is written to the registry.
- It brings a window to the front, minimizes it or closes it, on the user's click. Everything it
  can do to a window, the Windows taskbar can do as well.

## What is already known

These are documented. A report of one tells us nothing that is not already written down.

- **The executable is not code-signed**, so SmartScreen warns the first time it is run. A
  certificate is the fix and one has not been bought
  ([#71](https://github.com/kaorinstar/windows-simple-tasktabbar/issues/71)).
- **The three activation strategies work around the Windows foreground lock.** They use
  documented API — `SetForegroundWindow`, `AttachThreadInput`, and an Alt press and release
  through `keybd_event` — from a process the user started themselves, and they gain no privilege
  by it: a window that was out of reach is still one the same user already owns.
  [docs/architecture.md](docs/architecture.md) describes the sequence.
- **A window owned by an elevated application cannot be fully controlled.** Windows integrity
  levels stop a normal process from acting on one. That is the restriction working rather than a
  way around it.
