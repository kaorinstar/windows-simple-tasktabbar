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
; tells a Unicode script from one written in the machine's own code page, and without it the
; Japanese message below is read as whatever that code page happens to be.

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

; The bar holds its own executable open while it runs, so installing over a copy already there
; has to close it first. Setup asks Windows to close it, rather than telling the user to find
; the tray icon and exit it by hand. Asking is also what gets the desktop its space back: the
; bar unregisters itself as an AppBar while it closes, which a forced termination would skip.
CloseApplications=yes
; Setup does not start it again itself. The last page offers that instead, so an installation
; over a running bar ends the same way as a first installation.
RestartApplications=no

; The wizard's own text comes from the language files that ship with Inno Setup, not from
; Localization/UiStrings.cs, so the twelve languages the application is written in do not apply
; here. English and Japanese are the two someone on this project reads, and those are the two
; offered. The dialog that asks which to use appears only when Windows is set to neither.
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.StartWithWindows=Start the bar when Windows starts
japanese.StartWithWindows=Windows の起動時にバーを開始する

[Tasks]
Name: "startupicon"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:AdditionalIcons}"

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

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; The settings file in %APPDATA%\WindowsSimpleTaskTabBar is deliberately not deleted here. It is
; the user's own work - their language, colours, groups and tab order - and uninstalling to
; install a newer version is the ordinary reason to uninstall at all.
