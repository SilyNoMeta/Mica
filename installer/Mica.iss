; Inno Setup script for Mica.
;
; Mica installs per user, into %LOCALAPPDATA%\Programs\Mica, and never asks for
; elevation. That is not only politeness: Mica writes a small executable into its
; own install folder for every app the user creates, so the folder has to stay
; writable by that user.
;
; Build:
;   powershell -ExecutionPolicy Bypass -File installer\Publish.ps1
;   iscc installer\Mica.iss /DAppVersion=1.1.3

#ifndef AppVersion
  #define AppVersion "1.1.3"
#endif

#ifndef Architecture
  #define Architecture "x64"
#endif

#define AppName "Mica"
#define AppPublisher "Mica"
#define AppExeName "Mica.Builder.exe"
#define AppUserModelId "Mica.Builder"
#define PackExtension ".mica"
#define PackProgId "Mica.AppPack"
#define RuntimeUrl "https://aka.ms/windowsappsdk/2.3/latest/windowsappruntimeinstall-x64.exe"

[Setup]
AppId={{6F3B1C0E-6C2E-4F1D-9E5B-4B2F0B7A9C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\dist
OutputBaseFilename=MicaSetup-{#AppVersion}-{#Architecture}
SetupIconFile=..\Mica.Builder\Assets\AppIcon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
; "x64compatible" rather than "x64os": an ARM64 machine runs the x64 build under
; emulation perfectly well, and excluding it would strand those users. Requires
; Inno Setup 6.3 or newer, which the workflow guarantees.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Per user: no UAC prompt, and the install folder stays writable so Mica can add
; a launcher there each time the user creates an app.
PrivilegesRequired=lowest

; Mica's own window plus every generated app runs from this folder, so an upgrade
; has to close them all before replacing files.
CloseApplications=yes
RestartApplications=no

; Mica claims .mica, so Setup notifies the shell instead of leaving Explorer to
; show the old icon until it next restarts.
ChangesAssociations=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
; Only the placeholder: Mica rewrites this on first run in whichever of its twelve
; languages the user reads, which Setup has no way to know.
en.PackFileType=Mica apps
fr.PackFileType=Apps Mica
en.RuntimeInstalling=Installing the Windows App Runtime...
en.RemoveAppsTitle=Remove the apps you created?
en.RemoveAppsPrompt=Mica created separate apps on this computer. Removing them also deletes their browser profiles, so you will be signed out of every site they open.%n%nRemove them? Choosing No leaves them installed and working.
fr.RuntimeInstalling=Installation du Windows App Runtime...
fr.RemoveAppsTitle=Supprimer les apps créées ?
fr.RemoveAppsPrompt=Mica a créé des apps distinctes sur cet ordinateur. Les supprimer efface aussi leurs profils de navigation : vous serez déconnecté de tous les sites qu'elles ouvrent.%n%nLes supprimer ? En choisissant Non, elles restent installées et fonctionnelles.

[Files]
Source: "..\dist\payload-{#Architecture}\*"; DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; The AppUserModelID has to match what the builder sets on its window, or Windows
; treats the shortcut and the running app as two different things.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; AppUserModelID: "{#AppUserModelId}"

[Registry]
; Double-clicking an app pack opens it in Mica. Declared here so that uninstalling
; gives the file type back — Mica rewrites these on every run, where it can put the
; type name in the user's own language, but only Setup knows when it is leaving.
Root: HKCU; Subkey: "Software\Classes\{#PackExtension}"; \
    ValueType: string; ValueName: ""; ValueData: "{#PackProgId}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#PackExtension}\OpenWithProgids"; \
    ValueType: none; ValueName: "{#PackProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\{#PackProgId}"; \
    ValueType: string; ValueName: ""; ValueData: "{cm:PackFileType}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#PackProgId}\DefaultIcon"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"",0"
Root: HKCU; Subkey: "Software\Classes\{#PackProgId}\shell\open"; \
    ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"
Root: HKCU; Subkey: "Software\Classes\{#PackProgId}\shell\open\command"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
Filename: "{tmp}\WindowsAppRuntimeInstall.exe"; Parameters: "--quiet"; \
    StatusMsg: "{cm:RuntimeInstalling}"; Check: NeedsRuntime; Flags: waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent

; Mica updates itself by running this silently and quitting, so it cannot reopen
; itself afterwards — its files are being replaced. /relaunch=1 asks Setup to do it.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: ShouldRelaunch

[UninstallRun]
; Runs while the files are still present. The builder removes each generated app:
; its shortcut, launcher, browser profile and Installed-apps entry.
Filename: "{app}\{#AppExeName}"; Parameters: "--uninstall-all"; \
    RunOnceId: "RemoveGeneratedApps"; Check: ShouldRemoveApps; Flags: waituntilterminated runhidden

[UninstallDelete]
; Launchers are written after install, so Setup does not know about them.
Type: filesandordirs; Name: "{app}"

[Code]
var
  RuntimeMissing: Boolean;
  RuntimeChecked: Boolean;
  DownloadPage: TDownloadWizardPage;

{ The Windows App Runtime ships as an MSIX framework package and leaves no
  registry marker to test, so ask the package manager directly. }
function NeedsRuntime: Boolean;
var
  ExitCode: Integer;
begin
  if not RuntimeChecked then
  begin
    RuntimeChecked := True;
    RuntimeMissing := True;

    if Exec('powershell.exe',
            '-NoProfile -NonInteractive -Command "exit [int](-not (Get-AppxPackage -Name Microsoft.WindowsAppRuntime.2))"',
            '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RuntimeMissing := ExitCode <> 0;
  end;

  Result := RuntimeMissing;
end;

function OnDownloadProgress(const Url, Filename: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = wpReady) and NeedsRuntime then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#RuntimeUrl}', 'WindowsAppRuntimeInstall.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

{ Deleting someone's apps — and with them their signed-in sessions — is not a
  side effect of uninstalling Mica. Ask, and default to keeping them. }
function ShouldRemoveApps: Boolean;
begin
  Result := SuppressibleMsgBox(
    ExpandConstant('{cm:RemoveAppsPrompt}'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;
