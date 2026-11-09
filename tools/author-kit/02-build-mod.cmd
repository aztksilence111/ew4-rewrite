@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0author.ps1" -Action Build -Project "%~1" -NonInteractive
set "EW4_AUTHOR_EXIT=%ERRORLEVEL%"
if /I not "%~2"=="--no-pause" pause
exit /b %EW4_AUTHOR_EXIT%
