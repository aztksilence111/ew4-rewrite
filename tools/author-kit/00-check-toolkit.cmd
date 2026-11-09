@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0author.ps1" -Action KitCheck
set "EW4_AUTHOR_EXIT=%ERRORLEVEL%"
if /I not "%~1"=="--no-pause" pause
exit /b %EW4_AUTHOR_EXIT%
