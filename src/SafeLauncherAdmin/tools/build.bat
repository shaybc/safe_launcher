@echo off
setlocal

set "TOOLS_DIR=%~dp0"
set "PROJECT_DIR=%TOOLS_DIR%.."
for %%I in ("%PROJECT_DIR%") do set "PROJECT_DIR=%%~fI"
set "SRC_DIR=%PROJECT_DIR%\.."
set "ROOT=%PROJECT_DIR%\..\.."
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net8.0-windows\win-x64\publish"
set "PUBLISHED_EXE=%PUBLISH_DIR%\SafeLauncherAdmin.exe"
set "ROOT_EXE=%ROOT%\safe_launcher_admin.exe"

echo Calling build outputs clear ...
call "%PROJECT_DIR%\tools\clear_build.bat"
echo .

echo Restoring SafeLauncher Admin packages...
dotnet restore "%PROJECT_DIR%\SafeLauncherAdmin.csproj" -r win-x64 --ignore-failed-sources --configfile "%SRC_DIR%\NuGet.config"

if errorlevel 1 (
	echo Restore failed due to packages could not be downloaded from the nuget repo, or local packages folder does not exist or missing packages.
	echo make sure the Artifactory url is valid and you have read access to the repo
	echo if running from an online pc - make sure the https://api.nuget.org/v3/index.json entry exists
	exit /b 1
)

echo Building SafeLauncher admin UI...
dotnet publish "%PROJECT_DIR%\SafeLauncherAdmin.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true --no-restore

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
