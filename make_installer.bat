@echo off
setlocal
echo ========================================================
echo Achievement Tracker - Professional Setup Creator
echo ========================================================
echo.

echo [1/2] Publishing self-contained release (Single File)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo [ERROR] dotnet publish failed!
    pause
    exit /b 1
)

echo.
echo [2/2] Checking for Inno Setup compiler (ISCC.exe)...
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo [WARNING] Inno Setup compiler (ISCC.exe) was not found automatically.
    echo Please install Inno Setup 6 from https://jrsoftware.org/isdl.php
    echo Once installed, you can compile installer.iss directly.
    echo.
    echo Alternatively, the published portable files are ready at:
    echo bin\Release\net8.0-windows\win-x64\publish\
    pause
    exit /b 0
)

echo Compiling installer with Inno Setup...
"%ISCC%" installer.iss
if errorlevel 1 (
    echo [ERROR] Inno Setup compilation failed!
    pause
    exit /b 1
)

echo.
echo ========================================================
echo SUCCESS! Setup installer created in ..\InstallerOutput\
echo ========================================================
pause
