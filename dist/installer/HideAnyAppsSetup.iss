; Hide Any Apps - Inno Setup script
; Build: powershell -ExecutionPolicy Bypass -File dist\build-release.ps1
;
; Per-user install (no UAC). The manager and service are both asInvoker, so a
; normal HKCU Run entry is enough for "start with Windows".

#define MyAppName      "Hide Any Apps"
#define MyAppVersion   "1.0.1"
#define MyAppPublisher "ziyyhhk"
#define MyAppURL       "https://github.com/ziyyhhk/Hide-Any-Apps"
#define MyAppExeName   "HideAnyWindowManager.exe"
#define MyServiceExe   "HideAnyWindowService.exe"
#define MyRunValue     "HideAnyAppsService"

[Setup]
; New AppId: this is a distinct per-user product, not an in-place upgrade of
; the old Program Files "Hide Any Window" install.
AppId={{7E2C4A91-5D3B-4C88-9F60-2B8A1D4E7C31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion=1.0.1.0
DefaultDirName={localappdata}\Programs\Hide Any Apps
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
OutputBaseFilename=HideAnyApps-Setup
OutputDir={#SourcePath}\..
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile={#SourcePath}\TERMS.txt
SetupIconFile={#SourcePath}\..\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
; The app is unsigned. SmartScreen may warn once; the README explains this.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start the background service when I sign in to Windows"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; Self-contained manager (.NET 8 + Windows App SDK + XAML resources)
Source: "{#SourcePath}\..\staging\manager\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Compiled AHK service (script embedded; asInvoker)
Source: "{#SourcePath}\..\staging\service\{#MyServiceExe}"; DestDir: "{app}"; Flags: ignoreversion
; License and readme
Source: "{#SourcePath}\..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#SourcePath}\..\..\README.md"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; The manager's Settings toggle writes/deletes this same value.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#MyRunValue}"; \
    ValueData: """{app}\{#MyServiceExe}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Start the background service immediately if autostart was selected. It is
; hidden (#NoTrayIcon) and keeps hiding apps after the manager is closed.
Filename: "{app}\{#MyServiceExe}"; Flags: runhidden nowait; Tasks: autostart; \
    StatusMsg: "Starting the background service..."
; Offer to open the manager.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Ask the service to restore every hidden window and exit before files are
; deleted. This uses the manager's CLI helper and waits for the mutex to clear.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--stop-service"; \
    Flags: runhidden waituntilterminated; RunOnceId: "StopService"
; Remove a Run entry the user may have enabled from the app after install.
Filename: "reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""{#MyRunValue}"" /f"; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveAutostart"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
