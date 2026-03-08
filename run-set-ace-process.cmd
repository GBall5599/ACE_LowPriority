@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "APP_EXE=%SCRIPT_DIR%ACE_LowPriority.exe"

if not exist "%APP_EXE%" (
    echo ERROR: "%APP_EXE%" not found.
    echo Please run build-exe.ps1 first.
    pause
    exit /b 1
)

"%APP_EXE%"
exit /b %ERRORLEVEL%
