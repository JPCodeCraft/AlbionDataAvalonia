#define MyAppName "Albion Free Market Data Client"
#define MyAppVersion "1.0"
#define MyAppExeName "AlbionDataAvalonia.Desktop.exe"
#define MyAppOutputDir "userdocs:Inno Setup Output"
#define MyAppOutputBaseFilename "AFMDataClientSetup"
#define MyAppSourceDir "C:\\Users\\augus\\source\\repos\\augusto501\\AlbionDataAvalonia\\AlbionDataAvalonia.Desktop\\bin\\Release\\net7.0\\*"
#define MyAppIconFile "C:\\Users\\augus\\source\\repos\\augusto501\\AlbionDataAvalonia\\AlbionDataAvalonia.Desktop\\bin\\Release\\net7.0\\Assets\\afm-logo.ico"
#define MyAppIconFileName "afm-logo.ico"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppIconFileName}
OutputDir={#MyAppOutputDir}
OutputBaseFilename={#MyAppOutputBaseFilename}
Compression=lzma
SolidCompression=yes
SetupIconFile={#MyAppIconFile}

[Files]
Source: "{#MyAppSourceDir}"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppIconFile}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconFileName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillMyApp"
