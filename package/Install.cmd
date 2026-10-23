@echo off
rem Installs HP Control Center to C:\Program Files\HPControlPanel (asks for administrator once).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
