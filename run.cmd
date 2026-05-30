@echo off
setlocal

set "ROOT=%~dp0"
set "ADMIN_EXE=%ROOT%safe_launcher_admin.exe"
set "ADMIN_BUILD=%ROOT%src\SafeLauncherAdmin\tools\build.bat"

if exist "%ADMIN_EXE%" (
    echo Running SafeLauncher Admin...
    start "" "%ADMIN_EXE%"
    exit /b 0
)

echo safe_launcher_admin.exe was not found.
echo Building SafeLauncher Admin...
echo.

call "%ADMIN_BUILD%"

if errorlevel 1 (
    echo.
    echo Build failed. Cannot run SafeLauncher Admin.
    exit /b 1
)

if not exist "%ADMIN_EXE%" (
    echo.
    echo Build completed, but safe_launcher_admin.exe was not found:
    echo %ADMIN_EXE%
    exit /b 1
)

echo.
echo Running SafeLauncher Admin...
start "" "%ADMIN_EXE%"

exit /b 0