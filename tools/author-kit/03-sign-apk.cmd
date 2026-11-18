@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign-apk.ps1" -Apk "%~1" -NonInteractive
set "EW4_SIGN_EXIT=%ERRORLEVEL%"
if /I not "%~2"=="--no-pause" pause
exit /b %EW4_SIGN_EXIT%
