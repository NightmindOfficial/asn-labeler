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

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "..\dist\asn-labeler.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}\licenses"
Source: "..\..\LICENSE-BPAC-EXCEPTION.txt"; DestDir: "{app}\licenses"
Source: "..\vendor\InnoSetup\LICENSE.txt"; DestDir: "{app}\licenses"; DestName: "InnoSetup.txt"

[Icons]
Name: "{group}\ASN Labeler"; Filename: "{app}\asn-labeler.exe"
Name: "{autodesktop}\ASN Labeler"; Filename: "{app}\asn-labeler.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\nightmind.dev\AsnLabeler"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\asn-labeler.exe"; Parameters: "--installed"; Description: "{cm:LaunchProgram,ASN Labeler}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
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
