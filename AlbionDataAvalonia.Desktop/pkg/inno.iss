#define MyAppName "Albion Free Market Data Client"
#define MyAppVersion "1.0"
#define MyAppExeName "AlbionDataAvalonia.Desktop.exe"
#define MyAppOutputDir "userdocs:Inno Setup Output"
#define MyAppOutputBaseFilename "AFMDataClientSetup"
#define MyAppSourceDir "C:\\Users\\augus\\source\\repos\\augusto501\\AlbionDataAvalonia\\AlbionDataAvalonia.Desktop\\bin\\Release\\net7.0\\*"
#define MyAppIconFile "C:\\Users\\augus\\source\\repos\\augusto501\\AlbionDataAvalonia\\AlbionDataAvalonia.Desktop\\bin\\Release\\net7.0\\Assets\\afm-logo.ico"
#define MyAppIconFilePath "Assets\afm-logo.ico"
#define WinPCapInstallerFile "C:\\Users\\augus\\source\\repos\\augusto501\\AlbionDataAvalonia\\AlbionDataAvalonia.Desktop\\bin\\Release\\net7.0\\ThirdParty\\WinPcap_4_1_3.exe"
#define WinPCapInstallerFilePath "ThirdParty\WinPcap_4_1_3.exe"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppIconFilePath}
OutputDir={#MyAppOutputDir}
OutputBaseFilename={#MyAppOutputBaseFilename}
Compression=lzma
SolidCompression=yes
SetupIconFile={#MyAppIconFile}
PrivilegesRequired=admin

[Files]
Source: "{#MyAppSourceDir}"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconFilePath}"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#WinPCapInstallerFilePath}"; Parameters: "/S"; StatusMsg: "Installing WinPcap..."; Flags: runhidden waituntilterminated

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillMyApp"
