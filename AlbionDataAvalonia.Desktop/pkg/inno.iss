#define MyAppId "AFMDataClient"
#define MyAppName "Albion Free Market Data Client"
#define MyAppPublisher "JP Code Craft"
#define MyAppPublisherURL "https://www.albionfreemarket.com"
#define MyAppVersion "0.1.4"
#define MyAppExeName "AlbionDataAvalonia.Desktop.exe"
#define MyAppOutputDir "userdocs:Inno Setup Output"
#define MyAppOutputBaseFilename "AFMDataClientSetup"
#define MyAppSourceDir "..\\bin\\Release\\net7.0\\*"
#define MyAppIconFile "..\\bin\\Release\\net7.0\\Assets\\afm-logo.ico"
#define MyAppIconFilePath "Assets\afm-logo.ico"
#define WinPCapInstallerFile "..\\bin\\Release\\net7.0\\ThirdParty\\WinPcap_4_1_3.exe"
#define WinPCapInstallerFilePath "ThirdParty\WinPcap_4_1_3.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppPublisherURL}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppIconFilePath}
OutputDir={#MyAppOutputDir}
OutputBaseFilename={#MyAppOutputBaseFilename}_v_{#MyAppVersion}
Compression=lzma
SolidCompression=yes
SetupIconFile={#MyAppIconFile}
WizardStyle=modern
PrivilegesRequired=admin

[Files]
Source: "{#MyAppSourceDir}"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconFilePath}"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#WinPCapInstallerFilePath}"; Parameters: "/S"; StatusMsg: "Installing WinPcap..."; Flags: runhidden waituntilterminated; Check: not IsWinPcapInstalled

[Code]
function IsWinPcapInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\WinPcap') or RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Npcap');
  if not Result then
    MsgBox('WinPcap is not currently installed on your system. We will proceed with the installation now. This is a necessary component for the application to function properly. Please follow the on-screen installation instructions.', mbInformation, MB_OK);
  //if Result then
  // MsgBox('WinPcap or Npcap is already installed on your system. No further action is needed.', mbInformation, MB_OK)
end;


[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillMyApp"
