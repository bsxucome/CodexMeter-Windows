@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows\scripts\collect-ci-logs.ps1"
echo.
echo You can close this window. Codex can read the log files in the project.
pause
