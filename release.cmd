@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Finish-Cycle.ps1" %*
exit /b %errorlevel%
