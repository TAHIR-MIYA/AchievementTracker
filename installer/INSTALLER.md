# Building the installer

You do this once on your own PC. The result is a single file, `AchievementTracker-Setup-1.0.0.exe`, that you can send to friends.

## Steps

1. Install Inno Setup 6 (free): `winget install JRSoftware.InnoSetup`
2. In PowerShell, from the project folder, run:

   ```
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\build-installer.ps1
   ```

   For a new version number: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\build-installer.ps1 -Version 1.1.0`
3. Your installer appears in `installer\Output\`.

`-ExecutionPolicy Bypass` applies only to the PowerShell process running this command; it does not change your system or user execution policy. If your organization enforces an execution policy, that policy may still prevent the script from running.

## What the installer does

* Installs for the current user only (no administrator prompt) into `%LOCALAPPDATA%\Programs\Achievement Tracker`.
* Includes the .NET runtime, so friends do not have to install anything else.
* Offers a Start menu shortcut (always), a desktop shortcut and Start with Windows (choices on the setup page).
* Closes a running copy before installing, so updating over an old version just works.
* Never ships your own game list or cached data, and an update never overwrites a user's data.
* Uninstalls cleanly and asks whether to also delete the user's data (default: keep it).

## Things to know

* **Windows SmartScreen** will say "Windows protected your PC" the first time a friend runs it, because the file is not code-signed (a certificate costs money). They click **More info, then Run anyway**.
* **Every friend needs their own free Steam Web API key** (the setup's last page explains where to get it).
* Change `MyAppPublisher` at the top of `AchievementTracker.iss` if you want a different name shown in Windows.
* Keep the `AppId` line in the `.iss` file the same forever. It is how Windows knows a new setup is an update.
