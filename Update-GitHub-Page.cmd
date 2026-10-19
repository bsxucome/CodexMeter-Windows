@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows\scripts\update-github-page.ps1"
echo.
echo You can close this window. Results are in windows\artifacts\github-page.
pause
