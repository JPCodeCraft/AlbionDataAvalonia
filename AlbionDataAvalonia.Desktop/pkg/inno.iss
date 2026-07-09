#define MyAppId "AFMDataClient"
#define MyAppName "Albion Free Market Data Client"
#define MyAppPublisher "JP Code Craft"
#define MyAppPublisherURL "https://www.albionfreemarket.com"
#define MyAppVersion "0.37.0.0"
#define MyAppExeName "AFMDataClient.exe"
#define MyAppOutputDir "userdocs:Inno Setup Output"
#define MyAppOutputBaseFilename "AFMDataClientSetup"
#define MyAppSourceDir "..\\bin\\Release\\net10.0-windows\\win-x64\\publish\\*"
#define MyAppIconFile "..\\bin\\Release\\net10.0-windows\\win-x64\\publish\\Assets\\afm-logo.ico"
#define MyAppIconFilePath "Assets\afm-logo.ico"
#define WinPCapInstallerFile "..\\bin\\Release\\net10.0-windows\\win-x64\\publish\\ThirdParty\\WinPcap_4_1_3.exe"
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

[Run]
Filename: "{app}\{#WinPCapInstallerFilePath}"; Parameters: "/S"; StatusMsg: "Installing WinPcap..."; Flags: shellexec runascurrentuser waituntilterminated; Check: not IsWinPcapInstalled
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillMyApp"

[Code]
var
  DeleteConfigFiles: Boolean;

function NextVersionPart(var Version: String): Integer;
var
  DotPosition: Integer;
  Part: String;
begin
  DotPosition := Pos('.', Version);
  if DotPosition = 0 then
  begin
    Part := Version;
    Version := '';
  end
  else
  begin
    Part := Copy(Version, 1, DotPosition - 1);
    Delete(Version, 1, DotPosition);
  end;

  Result := StrToIntDef(Part, 0);
end;

function CompareVersions(LeftVersion, RightVersion: String): Integer;
var
  Index: Integer;
  LeftPart: Integer;
  RightPart: Integer;
begin
  Result := 0;
  for Index := 1 to 4 do
  begin
    LeftPart := NextVersionPart(LeftVersion);
    RightPart := NextVersionPart(RightVersion);

    if LeftPart < RightPart then
    begin
      Result := -1;
      Exit;
    end;

    if LeftPart > RightPart then
    begin
      Result := 1;
      Exit;
    end;
  end;
end;

function GetInstalledVersion(var InstalledVersion: String): Boolean;
var
  UninstallKey: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  Result := RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', InstalledVersion);
  if not Result then
    Result := RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', InstalledVersion);
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
begin
  Result := True;
  if GetInstalledVersion(InstalledVersion) and
     (CompareVersions(InstalledVersion, '{#MyAppVersion}') > 0) then
  begin
    MsgBox(
      'A newer version (' + InstalledVersion + ') is already installed. ' +
      'Downgrading to {#MyAppVersion} is blocked to protect the local database.',
      mbError,
      MB_OK);
    Result := False;
  end;
end;

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
    DeleteConfigFiles := MsgBox('Do you want to remove your configuration files, including stored game E-MAILS?' + #13#10 + 
                                'Click YES to remove all data or NO to keep your configuration and emails.', mbConfirmation, MB_YESNO) = IDYES;
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');
    if DeleteConfigFiles then
    begin
      DelTree(ExpandConstant('{localappdata}\AFMDataClient'), True, True, True);
    end;
  end;
end;
