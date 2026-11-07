; Achievement Tracker installer. Needs Inno Setup 6.3 or newer (free, https://jrsoftware.org/isdl.php).
; Do not run this file directly: use build-installer.ps1, which publishes the app first and then compiles this script.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "Achievement Tracker"
#define MyAppPublisher "Tahir"
#define MyAppExe "AchievementTracker.exe"

[Setup]
; Keep this AppId the same forever: it is how Windows recognises an upgrade of the same app.
AppId={{919CB451-90A5-462A-B8BC-4D7AF0FD718A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Installs for the current user only: no administrator prompt, and the app can write its own files next to itself.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=AchievementTracker-Setup-{#MyAppVersion}
SetupIconFile=..\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
InfoAfterFile=after-install.txt
; The app lives in the tray, so Windows' own "close applications" check can't see it. The [Code] section stops it instead.
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "&Start Achievement Tracker when I sign in to Windows (recommended, so no unlock is missed)"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; Your own game list and cached data are NOT shipped, so an update never overwrites a user's data.
Source: "publish\*"; DestDir: "{app}"; Excludes: "tracked_games.json,achievement_dictionary.json,achievement_icons.json,achievement_rarity.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; The same entry the app's own "Start with Windows" switch uses, so the two always agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AchievementTracker"; ValueData: """{app}\{#MyAppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Files the app creates by itself next to the exe
Type: files; Name: "{app}\achievement_dictionary.json"
Type: files; Name: "{app}\achievement_icons.json"
Type: files; Name: "{app}\achievement_rarity.json"

[Code]
procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Before installing or updating: close the app if it is running in the tray.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  StopRunningApp;
  Result := True;
end;

// After uninstalling: offer to remove the user's own data too (default answer: keep it).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Do you also want to delete your Achievement Tracker data (game list, settings, unlock history and cached covers)?' + #13#10 + #13#10 +
              'Your screenshots and backup zips are not touched.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\AchievementTracker'), True, True, True);
      DeleteFile(ExpandConstant('{app}\tracked_games.json'));
      RemoveDir(ExpandConstant('{app}'));
    end;
  end;
end;
