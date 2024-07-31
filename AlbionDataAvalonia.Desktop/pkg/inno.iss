#define MyAppId "AFMDataClient"
#define MyAppName "Albion Free Market Data Client"
#define MyAppPublisher "JP Code Craft"
#define MyAppPublisherURL "https://www.albionfreemarket.com"
#define MyAppVersion "0.8.3.0"
#define MyAppExeName "AFMDataClient.exe"
#define MyAppOutputDir "userdocs:Inno Setup Output"
#define MyAppOutputBaseFilename "AFMDataClientSetup"
#define MyAppSourceDir "..\\bin\\Release\\net8.0\\publish\\*"
#define MyAppIconFile "..\\bin\\Release\\net8.0\\publish\\Assets\\afm-logo.ico"
#define MyAppIconFilePath "Assets\afm-logo.ico"
#define WinPCapInstallerFile "..\\bin\\Release\\net8.0\\publish\\ThirdParty\\WinPcap_4_1_3.exe"
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
PrivilegesRequired=lowest

[Files]
Source: "{#MyAppSourceDir}"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconFilePath}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#WinPCapInstallerFilePath}"; Parameters: "/S"; StatusMsg: "Installing WinPcap..."; Flags: shellexec runascurrentuser waituntilterminated; Check: not IsWinPcapInstalled
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillMyApp"

[Code]
var
  DeleteConfigFiles: Boolean;

function IsWinPcapInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\WinPcap') or RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Npcap') or RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\Win10Pcap');
  if not Result then
    MsgBox('WinPcap is not currently installed on your system. We will proceed with the installation now. This is a necessary component for the application to function properly. Please follow the on-screen installation instructions.', mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteConfigFiles := MsgBox('Do you want to remove your configuration files, including stored game emails?' + #13#10 + 
                                'Click Yes to remove all data or No to keep your configuration.', mbConfirmation, MB_YESNO) = IDYES;
  end
  else if (CurUninstallStep = usPostUninstall) and DeleteConfigFiles then
  begin
    DelTree(ExpandConstant('{localappdata}\AFMDataClient'), True, True, True);
  end;
end;