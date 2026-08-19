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
