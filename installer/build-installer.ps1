<#
  Builds the Achievement Tracker setup file.

  Usage (in PowerShell, from the project folder):
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\build-installer.ps1
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\build-installer.ps1 -Version 1.2.0

  It does two things:
    1. publishes the app as a self-contained Windows build (friends do not need to install .NET), and
    2. compiles installer\AchievementTracker.iss with Inno Setup into  installer\Output\AchievementTracker-Setup-<version>.exe

  You need the .NET 8 SDK (you already have it) and Inno Setup 6 (free):  winget install JRSoftware.InnoSetup
#>
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$projectFolder = Split-Path -Parent $PSScriptRoot
$publishFolder = Join-Path $PSScriptRoot "publish"

if (-not (Test-Path (Join-Path $projectFolder "AchievementTracker.csproj"))) {
    throw "Could not find AchievementTracker.csproj in $projectFolder. Keep the 'installer' folder inside the project folder."
}

# Look for the Inno Setup compiler before doing the slow part
$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host "Inno Setup 6 was not found." -ForegroundColor Yellow
    Write-Host "Install it (free) with:   winget install JRSoftware.InnoSetup"
    Write-Host "or from https://jrsoftware.org/isdl.php  and then run this script again."
    exit 1
}

Write-Host "1/2  Publishing the app (version $Version)..." -ForegroundColor Cyan
if (Test-Path $publishFolder) { Remove-Item $publishFolder -Recurse -Force }
dotnet publish "$projectFolder\AchievementTracker.csproj" -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o $publishFolder
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (see the messages above)." }

Write-Host "2/2  Building the installer..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot "AchievementTracker.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (see the messages above)." }

$setup = Join-Path $PSScriptRoot "Output\AchievementTracker-Setup-$Version.exe"
Write-Host ""
Write-Host "Done. Your installer is here:" -ForegroundColor Green
Write-Host "  $setup"
Write-Host ("  Size: {0:N0} MB" -f ((Get-Item $setup).Length / 1MB))
