@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "SEARCH_ROOT=%SCRIPT_DIR%.."
set "APP_EXE="

for /r "%SEARCH_ROOT%" %%I in (ACE_LowPriority.exe) do (
    if /i not "%%~fI"=="%~f0" (
        set "APP_EXE=%%~fI"
        goto :found
    )
)

:found
if not defined APP_EXE (
    echo ERROR: ACE_LowPriority.exe not found under "%SEARCH_ROOT%".
    echo Please run build-exe.ps1 first.
    pause
    exit /b 1
)

"%APP_EXE%"
exit /b %ERRORLEVEL%
