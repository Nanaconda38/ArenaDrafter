#define AppName "ArenaDrafter"
#define AppVersion "0.1.0"
#define AppPublisher "Nanaconda38"
#define AppUrl "https://github.com/Nanaconda38/ArenaDrafter"
#define PublishDir "..\artifacts\release\ArenaDrafter-v0.1.0-win-x64-portable"

[Setup]
AppId={{6B53E328-5B2B-4E1D-9A8C-673ECE46D096}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=..\artifacts\release
OutputBaseFilename=ArenaDrafter-Setup-v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\ArenaDrafter.exe
VersionInfoVersion=0.1.0.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\ArenaDrafter.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\RslArenaProbe.dll"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{localappdata}\ArenaDrafter"; Flags: uninsneveruninstall
Name: "{localappdata}\ArenaDrafter\cache"; Flags: uninsneveruninstall
Name: "{localappdata}\ArenaDrafter\logs"; Flags: uninsneveruninstall
Name: "{localappdata}\ArenaDrafter\reports"; Flags: uninsneveruninstall

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\ArenaDrafter.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ArenaDrafter.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ArenaDrafter.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
