@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pack.ps1"
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" (
    echo Pack failed, exit code %EXITCODE%.
) else (
    echo Output: %~dp0publish\ForgeDeck.App.exe
)

if /i "%~1"=="-NoPause" goto :end
pause

:end
exit /b %EXITCODE%
