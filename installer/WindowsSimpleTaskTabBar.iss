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

; The name of the mutex Program.cs creates to keep a second bar from starting. Naming it here
; makes Setup and the uninstaller say "close it now, then click OK" when the bar is running,
; instead of running into whatever the running bar is holding open. It is the message rather
; than the failure: an uninstall that meets the running bar reports that a file is in use by
; another process, which tells the user nothing they can act on.
AppMutex=WindowsSimpleTaskTabBar_SingleInstance

; The bar holds its own executable open while it runs, so installing over a copy already there
; has to close it first. Once the user has answered the message above, Setup asks Windows to
; close anything still holding a file, rather than stopping. Asking is also what gets the
; desktop its space back: the bar unregisters itself as an AppBar while it closes, which a
; forced termination would skip.
CloseApplications=yes
; Setup does not start it again itself. The last page offers that instead, so an installation
; over a running bar ends the same way as a first installation.
RestartApplications=no

; The wizard's own text comes from the language files that ship with Inno Setup, not from
; Localization/UiStrings.cs. Nothing here is translated by this project, so the list is the
; languages the bar is written in and Inno Setup carries a file for, in the order
; Localization/Languages.cs lists them.
;
; Simplified and Traditional Chinese are the two the bar has and this list does not. Inno Setup
; ships no file for either, and the translations that exist for them are maintained elsewhere.
; Taking a copy of one would mean carrying wizard text nobody here can read, against the rule
; that a language is corrected by someone who reads it. Those two see the wizard in English and
; the bar itself in Chinese.
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
; ten times by whoever added the eleventh language.
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
