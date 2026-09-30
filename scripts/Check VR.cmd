@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Preflight.ps1" -VR
pause
