@echo off
start "" /b powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" -InstallRoot "%~dp0."
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" if not defined DESKTOP_MANAGER_NO_PAUSE pause
exit /b %EXIT_CODE%
