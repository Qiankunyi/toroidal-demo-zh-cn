@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
set "install_status=%errorlevel%"
pause
exit /b %install_status%
