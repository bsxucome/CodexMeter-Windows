@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows\scripts\sync-ci-fix.ps1"
echo.
echo You can close this window. Codex can read the CI result in the project.
pause
