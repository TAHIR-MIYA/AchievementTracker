[Setup]
AppName=Achievement Tracker
AppVersion=1.0
DefaultDirName={autopf}\AchievementTracker
DefaultGroupName=Achievement Tracker
OutputDir=..\InstallerOutput
OutputBaseFilename=AchievementTracker_Setup
Compression=lzma2/ultra
SolidCompression=yes
PrivilegesRequired=lowest
SetupIconFile=app_icon.ico
UninstallDisplayIcon={app}\AchievementTracker.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Achievement Tracker"; Filename: "{app}\AchievementTracker.exe"
Name: "{group}\Uninstall Achievement Tracker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Achievement Tracker"; Filename: "{app}\AchievementTracker.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\AchievementTracker.exe"; Description: "{cm:LaunchProgram,Achievement Tracker}"; Flags: nowait postinstall skipifsilent
