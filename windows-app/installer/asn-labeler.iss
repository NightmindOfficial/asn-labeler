#ifndef AppVersion
  #error AppVersion is required
#endif
[Setup]
AppId={{EF97729F-626F-4E2D-8B1C-A7063921A2D0}
AppName=ASN Labeler
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
AppPublisher=nightmind.dev
AppPublisherURL=https://nightmind.dev
DefaultDirName={autopf}\ASN Labeler
DefaultGroupName=ASN Labeler
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
UsePreviousTasks=yes
ChangesEnvironment=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=asn-labeler-{#AppVersion}-setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\asn-labeler.exe
LicenseFile=..\..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\AsnLabelerBrotherQl800,Global\AsnLabelerRunning
SetupMutex=Global\AsnLabelerSetup
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.ScopeConflict=ASN Labeler is installed in another mode. Uninstall it before changing the installation mode. Settings will be retained.
de.ScopeConflict=ASN Labeler ist in einem anderen Modus installiert. Bitte vor dem Wechsel des Installationsmodus deinstallieren. Einstellungen bleiben erhalten.
en.CloseApp=Close ASN Labeler in all Windows sessions before installing. Running print jobs will not be interrupted.
de.CloseApp=Bitte ASN Labeler vor der Installation in allen Windows-Sitzungen beenden. Laufende Druckauftraege werden nicht unterbrochen.
en.NewerVersion=A newer ASN Labeler version is already installed.
de.NewerVersion=Eine neuere Version von ASN Labeler ist bereits installiert.
en.CliTask=Install command-line tool (asnlbl) and add it to PATH
de.CliTask=Kommandozeilenwerkzeug (asnlbl) installieren und zu PATH hinzufuegen

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "cli"; Description: "{cm:CliTask}"; Flags: unchecked

[Files]
Source: "..\dist\asn-labeler.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\asn-labeler-cli.exe"; DestDir: "{app}\cli"; DestName: "asnlbl.exe"; Flags: ignoreversion; Tasks: cli
Source: "..\..\docs\CLI.md"; DestDir: "{app}\cli"; Tasks: cli
Source: "..\..\LICENSE"; DestDir: "{app}\licenses"
Source: "..\..\LICENSE-BPAC-EXCEPTION.txt"; DestDir: "{app}\licenses"
Source: "..\vendor\InnoSetup\LICENSE.txt"; DestDir: "{app}\licenses"; DestName: "InnoSetup.txt"

[Icons]
Name: "{group}\ASN Labeler"; Filename: "{app}\asn-labeler.exe"
Name: "{autodesktop}\ASN Labeler"; Filename: "{app}\asn-labeler.exe"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{app}\asn-labeler-cli.exe"
Type: files; Name: "{app}\cli\asnlbl.exe"; Tasks: not cli
Type: files; Name: "{app}\cli\CLI.md"; Tasks: not cli

[Registry]
Root: HKA; Subkey: "Software\nightmind.dev\AsnLabeler"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\asn-labeler.exe"; Parameters: "--installed"; Description: "{cm:LaunchProgram,ASN Labeler}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  AppKey = 'Software\nightmind.dev\AsnLabeler';

function PathRoot(): Integer;
begin
  if IsAdminInstallMode then Result := HKLM64 else Result := HKCU;
end;

function EnvironmentKey(): String;
begin
  if IsAdminInstallMode then Result := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
  else Result := 'Environment';
end;

function PathWithout(Value, Folder: String; var Found: Boolean): String;
var
  Segment: String;
  P: Integer;
  First: Boolean;
begin
  Result := ''; Found := False; First := True;
  repeat
    P := Pos(';', Value);
    if P = 0 then begin Segment := Value; Value := ''; end
    else begin Segment := Copy(Value, 1, P - 1); Delete(Value, 1, P); end;
    if CompareText(RemoveBackslashUnlessRoot(RemoveQuotes(Trim(Segment))), Folder) = 0 then Found := True
    else begin
      if not First then Result := Result + ';';
      Result := Result + Segment; First := False;
    end;
  until P = 0;
end;

procedure RemoveOwnedCliPath();
var
  Owned, Value, Updated: String;
  Found: Boolean;
begin
  if not RegQueryStringValue(PathRoot(), AppKey, 'CliPathAdded', Owned) then Exit;
  if RegQueryStringValue(PathRoot(), EnvironmentKey(), 'Path', Value) then begin
    Updated := PathWithout(Value, Owned, Found);
    if Found and not RegWriteExpandStringValue(PathRoot(), EnvironmentKey(), 'Path', Updated) then
      RaiseException('Could not remove CLI PATH entry.');
  end;
  RegDeleteValue(PathRoot(), AppKey, 'CliPathAdded');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder, Owned, Value, Ignored: String;
  Found: Boolean;
begin
  if CurStep <> ssPostInstall then Exit;
  Folder := ExpandConstant('{app}\cli');
  RegQueryStringValue(PathRoot(), AppKey, 'CliPathAdded', Owned);
  if not WizardIsTaskSelected('cli') then begin RemoveOwnedCliPath(); Exit; end;
  if (Owned <> '') and (CompareText(Owned, Folder) <> 0) then RemoveOwnedCliPath();
  RegQueryStringValue(PathRoot(), EnvironmentKey(), 'Path', Value);
  Ignored := PathWithout(Value, Folder, Found);
  if not Found then begin
    if (Value <> '') and (Copy(Value, Length(Value), 1) <> ';') then Value := Value + ';';
    if not RegWriteStringValue(PathRoot(), AppKey, 'CliPathAdded', Folder) then
      RaiseException('Could not record CLI PATH ownership.');
    if not RegWriteExpandStringValue(PathRoot(), EnvironmentKey(), 'Path', Value + Folder) then
      RaiseException('Could not add CLI directory to PATH.');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemoveOwnedCliPath();
end;

function InitializeSetup(): Boolean;
var
  UserPath, MachinePath: String;
begin
  Result := True;
  RegQueryStringValue(HKCU, 'Software\nightmind.dev\AsnLabeler', 'InstallPath', UserPath);
  RegQueryStringValue(HKLM64, 'Software\nightmind.dev\AsnLabeler', 'InstallPath', MachinePath);
  if ((UserPath <> '') and IsAdminInstallMode) or
     ((MachinePath <> '') and not IsAdminInstallMode) then
  begin
    MsgBox(CustomMessage('ScopeConflict'), mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingVersion, NewVersion: Int64;
begin
  Result := '';
  if CheckForMutexes('Global\AsnLabelerRunning,Local\AsnLabelerBrotherQl800') then
    Result := CustomMessage('CloseApp');
  if GetPackedVersion(ExpandConstant('{app}\asn-labeler.exe'), ExistingVersion) and StrToVersion('{#AppVersion}.0', NewVersion) then
    if ComparePackedVersion(ExistingVersion, NewVersion) > 0 then
      Result := CustomMessage('NewerVersion');
end;
