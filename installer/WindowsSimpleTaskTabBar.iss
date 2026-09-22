; The installer attached to every release. It is built by the "Build the installer" step in
; .github/workflows/release.yml, which comes to this:
;
;     ISCC /DAppVersion=0.7.0 installer\WindowsSimpleTaskTabBar.iss
;
; The version is passed in rather than written here. One number - the tag - then decides the
; version inside the executable, the version the installer reports to Windows, and the version
; in the file name it is published under, so none of the three can drift from the others.
;
; The executable it packages is the published net48 build in artifacts\, so `dotnet publish`
; runs before this does.
;
; This file is stored as UTF-8 with a byte order mark. That mark is how the Inno Setup compiler
; tells a Unicode script from one written in the machine's own code page. Nothing here is
; outside ASCII today, and the mark stays so that the first line that is - an application name,
; a path, a message - cannot arrive as whatever that code page makes of it.

#ifndef AppVersion
  #error "AppVersion is not defined. Build this script with ISCC /DAppVersion=0.7.0"
#endif

#define AppName "WindowsSimpleTaskTabBar"
#define AppExe "WindowsSimpleTaskTabBar.exe"
#define AppPublisher "Kaoru Ishikura"
#define AppUrl "https://github.com/kaorinstar/windows-simple-tasktabbar"
#define PackageDir "..\artifacts"

[Setup]
; Windows recognizes an installed copy by this number, so it never changes. A new one would
; make the next installer a second application standing beside the one already installed, each
; with its own entry in Apps & features and neither aware of the other.
AppId={{83B40BD7-4145-4B27-BA11-A58117BE6F2F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 Kaoru Ishikura

; Installed for the user who runs it, into their own folder, so nothing here asks for
; administrator rights and no consent prompt appears. Elevation would buy nothing: the
; application writes its settings to %APPDATA% and the shortcut that starts it with Windows is
; the user's own. It also has a cost, because a file under Program Files cannot be replaced by
; the user who installed it, and the bar tells that user when a newer release exists.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\WindowsSimpleTaskTabBar\Properties\app.ico
LicenseFile=..\LICENSE

; .NET Framework 4.8 ships with Windows 10 version 1903 and later, which is the bound the
; README gives. 10.0.18362 is that version, so an older machine is turned away by the installer
; rather than by an application that will not start.
MinVersion=10.0.18362

OutputDir={#PackageDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

; There is deliberately no AppMutex here. It named the mutex Program.cs creates, and that made
; Setup and the uninstaller stop at their own start and ask the user to close the bar by hand.
; The check runs before Windows has been asked to close anything, so naming the mutex took the
; automatic close away before it could happen (#119). The [Code] section at the end of this file
; does what AppMutex did, one step later in each case, and closes the bar itself rather than
; asking the user to.

; The bar holds its own executable open while it runs, so installing over a copy already there
; has to close it first. Setup asks Windows to do that on the Preparing to Install page, once
; the user has chosen to install. Asking is also what gets the desktop its space back: the bar
; unregisters itself as an AppBar while it closes, which a forced termination would skip. That
; is why this is yes rather than force, which terminates whatever does not close.
CloseApplications=yes
; Setup does not start it again itself, and Windows cannot: restarting an application it closed
; needs that application to have called RegisterApplicationRestart, and this one does not. The
; last page offers to start the bar instead, so an installation over a running bar ends the same
; way as a first installation.
RestartApplications=no

; The wizard's own text comes from the language files that ship with Inno Setup, not from
; Localization/UiStrings.cs. The list is the languages of Localization/Languages.cs that the
; installed Inno Setup has a file for, in the order that file gives them. Nothing here is
; translated by this project, so a thirteenth language costs one line if Inno Setup carries it
; and nothing at all if it does not: an unmatched language sees the wizard in English.
;
; Simplified and Traditional Chinese are the two the bar has and this list does not.
; ChineseSimplified.isl and ChineseTraditional.isl are in Inno Setup's source tree, and the
; installer copies files\Languages\*.isl as it finds them, but the release Chocolatey puts on
; the runner does not carry either yet: naming them failed the compile with "Couldn't open
; include file". Adding them back means shipping a copy of each .isl beside this script, matched
; to the compiler's version, and until someone does that those two see the wizard in English
; while the bar itself is in Chinese.
;
; A file named here that the installed Inno Setup does not have fails the compile, so the
; "Build the installer" step of release.yml is what proves this list. Run it by hand on a branch
; before a tag: the same failure after a tag leaves a tag with no release under it.
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

; Both messages are Inno Setup's own, so the checkbox and the heading above it arrive translated
; in every language listed above. A sentence written here instead would have to be translated
; twelve times by whoever added the thirteenth language.
[Tasks]
Name: "startupicon"; Description: "{cm:AutoStartProgram,{#AppName}}"; GroupDescription: "{cm:AutoStartProgramGroupDescription}"

[Files]
Source: "{#PackageDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.ja.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
; The Start with Windows checkbox is this shortcut and nothing else: no registry value, no
; scheduled task. The uninstaller removes every shortcut it created, so nothing of it is left
; in shell:startup afterwards.
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startupicon

; shellexec, not the ordinary launch. Setup still has the uninstall log open when this runs, and
; a program it starts as its own child can be handed that file along with it, which leaves the
; bar holding the log for as long as it runs. The uninstaller then cannot read its own log and
; stops with "the process cannot access the file", naming a file the user has never heard of.
; Going through the shell starts the bar with nothing of Setup's passed on to it.
[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: shellexec nowait postinstall skipifsilent

; The settings file in %APPDATA%\WindowsSimpleTaskTabBar is deliberately not deleted here. It is
; the user's own work - their language, colours, groups and tab order - and uninstalling to
; install a newer version is the ordinary reason to uninstall at all.

; Closing a running bar so that its executable can be replaced or removed. This is what AppMutex
; used to stand in for, and it runs at the two points where Inno Setup lets a script stop the
; work: after the user has chosen to install, and after the user has confirmed the uninstall.
; Nothing here says anything of its own. The one message it can show is Inno Setup's own, so it
; arrives translated in every language listed above, in the same words the mutex used to show.
[Code]
const
  { The mutex Program.cs creates to keep a second bar from starting. While it exists a bar is
    running, and it is gone once that process has ended. The window disappears before the
    process does, so this is what the wait below watches rather than the window. }
  BarMutex = 'WindowsSimpleTaskTabBar_SingleInstance';
  WM_CLOSE = $0010;
  { Ten seconds, in the hundred-millisecond steps the wait is made of. Closing takes a fraction
    of that; the rest is for a machine busy enough to make a fraction take longer. }
  CloseSteps = 100;

{ Asks a running bar to close and waits for it to go. True when no bar is running, either
  because none was or because the one that was has closed. }
function CloseRunningBar(): Boolean;
var
  Wnd: HWND;
  I: Integer;
begin
  if not CheckForMutexes(BarMutex) then begin
    Result := True;
    Exit;
  end;

  { WM_CLOSE rather than a forced termination, for the reason CloseApplications is yes rather
    than force: the bar unregisters itself as an AppBar on its way out, and the desktop only
    gets that space back if that runs. The window is MainForm's, and its title is the
    application name, set in the MainForm constructor and not translated.

    There is one such window per monitor, and they all carry that title. Closing any one of
    them closes the application, and every other bar unregisters itself on the way out with it,
    so the first window found is the only one this has to post to. }
  Wnd := FindWindowByWindowName('{#AppName}');
  if Wnd <> 0 then
    PostMessage(Wnd, WM_CLOSE, 0, 0);

  for I := 1 to CloseSteps do begin
    Sleep(100);
    if not CheckForMutexes(BarMutex) then begin
      Result := True;
      Exit;
    end;
  end;

  Result := False;
end;

{ Closes the bar, and falls back to asking the user when it will not close: a window it is
  waiting on, a bar started by another account, anything this cannot reach. The message is Inno
  Setup's own msgSetupAppRunningError or msgUninstallAppRunningError, which is the sentence
  AppMutex used to show. Cancel ends Setup or the uninstaller, which is what Abort does at both
  of the two points this is called from, and nowhere else. A silent run has nobody to answer, so
  the message is suppressed there and taken as Cancel. }
procedure CloseRunningBarOrAsk(RunningMessage: String);
begin
  while not CloseRunningBar() do
    if SuppressibleMsgBox(FmtMessage(RunningMessage, ['{#AppName}']), mbError, MB_OKCANCEL,
       IDCANCEL) <> IDOK then
      Abort;
end;

{ Just before the files are copied. Windows has usually closed the bar by now, on the Preparing
  to Install page; this is here for the run where it could not, and for the silent install,
  where that page is never shown. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    CloseRunningBarOrAsk(SetupMessage(msgSetupAppRunningError));
end;

{ Just before the uninstaller checks for a running application, which is after the user has
  confirmed the uninstall and before anything has been deleted. The uninstaller never asks
  Windows to close anything - Restart Manager is Setup's alone - so this is the only thing
  between an uninstall and a running bar, and #107 is what that used to look like. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usAppMutexCheck then
    CloseRunningBarOrAsk(SetupMessage(msgUninstallAppRunningError));
end;
