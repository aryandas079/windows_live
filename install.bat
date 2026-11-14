@echo off
setlocal EnableDelayedExpansion
title Windows Live Dynamic Island - Installer

echo =======================================================
echo          Windows Live Dynamic Island - Installer
echo =======================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "PUBLISH_DIR=%SCRIPT_DIR%publish"
set "TARGET_DIR=%LOCALAPPDATA%\WindowsLive"
set "EXE_NAME=WindowsLive.exe"
set "TARGET_EXE=%TARGET_DIR%\%EXE_NAME%"
set "TASK_NAME=WindowsLiveDynamicIsland"

if not exist "%PUBLISH_DIR%\%EXE_NAME%" (
    echo [ERROR] Could not find %PUBLISH_DIR%\%EXE_NAME%
    echo Please make sure the project is built and the publish folder exists.
    pause
    exit /b 1
)

echo [1/4] Stopping any existing instances...
taskkill /F /IM %EXE_NAME% >nul 2>&1

echo [2/4] Installing application files to %TARGET_DIR%...
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"
xcopy /E /Y /I "%PUBLISH_DIR%\*" "%TARGET_DIR%\" >nul
if errorlevel 1 (
    echo [ERROR] Failed to copy files to %TARGET_DIR%
    pause
    exit /b 1
)

echo [3/4] Configuring automatic Windows startup...
:: 1. Add to Current User Run Registry Key (native Windows startup)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "WindowsLiveDynamicIsland" /t REG_SZ /d "\"%TARGET_EXE%\"" /f >nul

:: 2. Pre-approve in StartupApproved (ensures Windows never delays or disables it)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" /v "WindowsLiveDynamicIsland" /t REG_BINARY /d 020000000000000000000000 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder" /v "WindowsLiveDynamicIsland.lnk" /t REG_BINARY /d 020000000000000000000000 /f >nul 2>&1

:: 3. Create direct shortcut in Windows Startup Folder
set "STARTUP_FOLDER=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
set "SHORTCUT_PATH=%STARTUP_FOLDER%\WindowsLiveDynamicIsland.lnk"
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%SHORTCUT_PATH%'); $s.TargetPath = '%TARGET_EXE%'; $s.WorkingDirectory = '%TARGET_DIR%'; $s.Save()" >nul 2>&1

echo [4/4] Launching Windows Live Dynamic Island in background...
start "" /D "%TARGET_DIR%" "%TARGET_EXE%"

echo.
echo =======================================================
echo  Installation complete!
echo  Windows Live Dynamic Island is now active in the background.
echo  It will launch automatically whenever Windows starts.
echo  To stop and remove it, run uninstall.bat.
echo =======================================================
echo.
ping 127.0.0.1 -n 3 >nul
