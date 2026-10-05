; Inno Setup 6 script for ZipDrop.
; Built by ../build.ps1 (which passes /DAppVersion and /DPublishDir). Manual build:
;   iscc /DAppVersion=0.1.0 /DPublishDir=..\artifacts\publish installer\ZipDrop.iss
;
; Per-user install, no admin rights: ZipDrop must never run elevated (an elevated
; process cannot receive drag & drop from the normal, non-elevated Explorer).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{6B0E8B9C-3C1E-4C59-9F0D-5A1D2C7E4F11}
AppName=ZipDrop
AppVersion={#AppVersion}
AppPublisher=ZipDrop
DefaultDirName={localappdata}\Programs\ZipDrop
DefaultGroupName=ZipDrop
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=ZipDrop-Setup-{#AppVersion}
SetupIconFile=..\src\ZipDrop\Assets\ZipDrop.ico
UninstallDisplayIcon={app}\ZipDrop.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "sendto"; Description: "Add ZipDrop to the Explorer ""Send to"" menu"; Flags: checkedonce
Name: "startup"; Description: "Launch ZipDrop when Windows starts"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"

[Icons]
Name: "{userprograms}\ZipDrop"; Filename: "{app}\ZipDrop.exe"
Name: "{usersendto}\ZipDrop"; Filename: "{app}\ZipDrop.exe"; Tasks: sendto

[Registry]
; Same value the in-app "Launch at startup" setting writes, so both stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ZipDrop"; \
  ValueData: """{app}\ZipDrop.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ZipDrop"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\ZipDrop.exe"; Description: "Start ZipDrop"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM ZipDrop.exe /F"; Flags: runhidden; RunOnceId: "KillZipDrop"
