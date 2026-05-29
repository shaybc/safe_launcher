@echo off
setlocal

set "PROJECT_DIR=%~dp0"
for %%I in ("%PROJECT_DIR%") do set "PROJECT_DIR=%%~fI"
set "ROOT=%PROJECT_DIR%\..\.."
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "PUBLISH_DIR=%PROJECT_DIR%bin\Release\net8.0-windows\win-x64\publish"
set "PUBLISHED_EXE=%PUBLISH_DIR%\SafeLauncherAdmin.exe"
set "ROOT_EXE=%ROOT%\safe_launcher_admin.exe"

echo Building SafeLauncher admin UI...
dotnet publish "%PROJECT_DIR%SafeLauncherAdmin.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Copying admin UI to repository root...
copy /Y "%PUBLISHED_EXE%" "%ROOT_EXE%" >NUL

if errorlevel 1 (
    echo Failed to copy admin UI to %ROOT_EXE%.
    exit /b 1
)

echo.
echo Build succeeded.
echo Runnable admin UI:
echo %ROOT_EXE%

exit /b 0
