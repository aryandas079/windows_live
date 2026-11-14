@echo off
setlocal EnableDelayedExpansion
title Windows Live Dynamic Island - Uninstaller

echo =======================================================
echo         Windows Live Dynamic Island - Uninstaller
echo =======================================================
echo.

set "TARGET_DIR=%LOCALAPPDATA%\WindowsLive"
set "EXE_NAME=WindowsLive.exe"
set "TASK_NAME=WindowsLiveDynamicIsland"

echo [1/3] Terminating running instances of Windows Live Dynamic Island...
taskkill /F /IM %EXE_NAME% >nul 2>&1
timeout /t 1 >nul 2>&1
taskkill /F /IM %EXE_NAME% >nul 2>&1

echo [2/3] Removing automatic startup configuration...
:: Remove from registry Run key
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "WindowsLiveDynamicIsland" /f >nul 2>&1

:: Remove from registry StartupApproved
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" /v "WindowsLiveDynamicIsland" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder" /v "WindowsLiveDynamicIsland.lnk" /f >nul 2>&1

:: Remove from Windows Startup folder
set "STARTUP_FOLDER=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
if exist "%STARTUP_FOLDER%\WindowsLiveDynamicIsland.lnk" (
    del /f /q "%STARTUP_FOLDER%\WindowsLiveDynamicIsland.lnk" >nul 2>&1
)

:: Remove scheduled task if it was created
schtasks /delete /tn "%TASK_NAME%" /f >nul 2>&1

echo [3/3] Removing installed files...
if exist "%TARGET_DIR%" (
    rmdir /S /Q "%TARGET_DIR%" >nul 2>&1
)

echo.
echo =======================================================
echo  Uninstallation complete!
echo  Windows Live Dynamic Island has been stopped and removed.
echo =======================================================
echo.
ping 127.0.0.1 -n 3 >nul
