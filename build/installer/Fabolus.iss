; Fabolus installer
;
; Compiled by build/publish.ps1, which supplies:
;   /DAppVersion=<version>   e.g. 0.9.4
;   /DPayloadDir=<path>      the self-contained publish output to package
;   /DOutputDir=<path>       where Fabolus-<version>-setup.exe is written
;
; Requires Inno Setup 6.3 or later (for ArchitecturesAllowed=x64compatible):
;   winget install JRSoftware.InnoSetup

#ifndef AppVersion
  #error AppVersion is not defined. Build the installer through build/publish.ps1.
#endif
#ifndef PayloadDir
  #error PayloadDir is not defined. Build the installer through build/publish.ps1.
#endif
#ifndef OutputDir
  #error OutputDir is not defined. Build the installer through build/publish.ps1.
#endif

#define AppName        "Fabolus"
#define AppPublisher   "nsmela"
#define AppUrl         "https://github.com/nsmela/Fabolus"
#define AppExeName     "Fabolus.exe"
#define IconSource     "..\..\src\Fabolus.Wpf\Fabolus icon resized.ico"

[Setup]
; This GUID identifies Fabolus across versions. Never change it: it is what lets a
; new version upgrade an existing install in place instead of sitting alongside it.
AppId={{8FC8CE15-9EC2-4606-8D9A-F794CC4B0B23}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Per-user install by default: under PrivilegesRequired=lowest, {autopf} resolves to
; %LOCALAPPDATA%\Programs, so there is no UAC prompt and the install directory stays
; writable. That matters because the app saves its preferences next to the exe.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

SetupIconFile={#IconSource}
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
WizardStyle=modern

; If Fabolus is running, its files are locked and the install would fail part-way.
; Let the Restart Manager detect and close it first rather than erroring out.
CloseApplications=yes
RestartApplications=no

; The payload is a self-contained .NET build (~280 MB), so compression settings matter.
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4

OutputDir={#OutputDir}
OutputBaseFilename=Fabolus-{#AppVersion}-setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Preferences are written next to the exe at runtime, so remove them on uninstall.
Type: files; Name: "{app}\Fabolus.dll.config"
Type: dirifempty; Name: "{app}"

[Code]
{ Inno matches installs by AppId, so an upgrade already reuses the existing directory
  and Add/Remove entry. What it does NOT do on its own is tell the user what is about to
  happen, or stop an older build from silently replacing a newer one. }

const
  UninstallSubkey =
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8FC8CE15-9EC2-4606-8D9A-F794CC4B0B23}_is1';

var
  ExistingVersion: String;
  ExistingScope: String;

{ Look for a previous install: per-user first, then machine-wide in both registry views. }
function FindExistingInstall: Boolean;
begin
  Result := True;

  if RegQueryStringValue(HKEY_CURRENT_USER, UninstallSubkey, 'DisplayVersion', ExistingVersion) then
  begin
    ExistingScope := 'for your user account';
    Exit;
  end;

  if RegQueryStringValue(HKEY_LOCAL_MACHINE, UninstallSubkey, 'DisplayVersion', ExistingVersion) then
  begin
    ExistingScope := 'for all users';
    Exit;
  end;

  if IsWin64 and RegQueryStringValue(HKLM32, UninstallSubkey, 'DisplayVersion', ExistingVersion) then
  begin
    ExistingScope := 'for all users';
    Exit;
  end;

  ExistingVersion := '';
  ExistingScope := '';
  Result := False;
end;

{ Negative when the installed build is older than this one, 0 when equal, positive when newer. }
function CompareWithExisting: Integer;
var
  Installed, Incoming: Int64;
begin
  Result := 0;
  if StrToVersion(ExistingVersion, Installed) and StrToVersion('{#AppVersion}', Incoming) then
    Result := ComparePackedVersion(Installed, Incoming);
end;

function InitializeSetup: Boolean;
var
  Message: String;
begin
  Result := True;

  if not FindExistingInstall then
    Exit;

  { A silent run is scripted, so take it at its word and skip the prompts. }
  if WizardSilent then
    Exit;

  case CompareWithExisting of
    1:
      begin
        Message :=
          'Fabolus ' + ExistingVersion + ' is already installed ' + ExistingScope + ', which is'
          + ' newer than the Fabolus {#AppVersion} you are about to install.' + #13#10#13#10
          + 'Continuing will replace it with this older version.' + #13#10#13#10
          + 'Install the older version anyway?';
        Result := MsgBox(Message, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
      end;
    0:
      begin
        Message :=
          'Fabolus ' + ExistingVersion + ' is already installed ' + ExistingScope + '.'
          + #13#10#13#10
          + 'Continuing will reinstall it over the existing copy.' + #13#10#13#10
          + 'Do you want to continue?';
        Result := MsgBox(Message, mbConfirmation, MB_YESNO) = IDYES;
      end;
  end;
  { An ordinary upgrade needs no prompt; it is reported on the Ready page instead. }
end;

{ Surface what is happening to the existing install on the "Ready to Install" page. }
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  Existing: String;
begin
  Result := '';

  if ExistingVersion <> '' then
  begin
    case CompareWithExisting of
      1: Existing := 'Fabolus ' + ExistingVersion + ' (' + ExistingScope + ') will be replaced by'
                   + ' this older version {#AppVersion}.';
      0: Existing := 'Fabolus ' + ExistingVersion + ' (' + ExistingScope + ') will be reinstalled.';
    else
      Existing := 'Fabolus ' + ExistingVersion + ' (' + ExistingScope + ') will be upgraded to'
                + ' {#AppVersion}.';
    end;
    Result := 'Existing installation:' + NewLine + Space + Existing + NewLine + NewLine;
  end;

  Result := Result + MemoDirInfo + NewLine + NewLine + MemoGroupInfo;

  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;
