@echo off
rem Tykhyi PK: builds TyhyiPK.exe and the installer Tykhyi-PK-setup.exe (all the work is in build.ps1).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
echo.
pause
