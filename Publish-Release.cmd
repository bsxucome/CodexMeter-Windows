@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows\scripts\release-github.ps1"
set "releaseExit=%errorlevel%"
echo.
if not "%releaseExit%"=="0" echo Release did not complete. See the error above and the saved result.
echo Release result is saved in windows\artifacts\release-logs\latest-result.txt
pause
exit /b %releaseExit%
