@echo off
setlocal

set "TOOLS_DIR=%~dp0"
set "PROJECT_DIR=%TOOLS_DIR%.."
for %%I in ("%PROJECT_DIR%") do set "PROJECT_DIR=%%~fI"
set "SRC_DIR=%PROJECT_DIR%\.."
set "ROOT=%PROJECT_DIR%\..\.."
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "CONFIG_PATH=%PROJECT_DIR%\launcher-config.json"
set "GENERATED_CONFIG=%PROJECT_DIR%\Generated\LauncherConfig.g.cs"
set "LAUNCHER_OUTPUT_METADATA=%PROJECT_DIR%\Generated\LauncherOutput.cmd"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net8.0-windows\win-x64\publish"
set "PUBLISHED_EXE=%PUBLISH_DIR%\SafeLauncher.exe"

echo Calling build outputs clear ...
call "%PROJECT_DIR%\tools\clear_build.bat"
echo .

echo Generating embedded launcher config...
powershell -NoProfile -ExecutionPolicy Bypass -File "%TOOLS_DIR%generate-launcher-config.ps1" -ConfigPath "%CONFIG_PATH%" -OutputPath "%GENERATED_CONFIG%" -LauncherOutputMetadataPath "%LAUNCHER_OUTPUT_METADATA%"

if errorlevel 1 (
    echo.
    echo Config generation failed.
    exit /b 1
)

if exist "%LAUNCHER_OUTPUT_METADATA%" (
    call "%LAUNCHER_OUTPUT_METADATA%"
) else (
    echo.
    echo Config generation failed since "%LAUNCHER_OUTPUT_METADATA%" does not exist.
    exit /b 1
)

if not defined LAUNCHER_PACKAGE_NAME (
    echo.
    echo LAUNCHER_PACKAGE_NAME was not defined by LauncherOutput.cmd.
    exit /b 1
)

if not defined LAUNCHER_EXE_NAME (
    echo.
    echo LAUNCHER_EXE_NAME was not defined by LauncherOutput.cmd.
    exit /b 1
)

set "DIST_DIR=%ROOT%\dist\%LAUNCHER_PACKAGE_NAME%"
set "DIST_EXE=%DIST_DIR%\%LAUNCHER_EXE_NAME%"

if NOT defined LAUNCHER_ICON_PATH (
    set "LAUNCHER_ICON_PATH=%PROJECT_DIR%\img\Safe_Launcher.ico"
)

if not exist "%LAUNCHER_ICON_PATH%" (
    echo .
    echo Launcher Icon was not found at: %LAUNCHER_ICON_PATH%
    exit /b 1
)

if not exist "%SRC_DIR%\NuGet.config" (
    echo .
    echo NuGet.config was not found at: %SRC_DIR%\NuGet.config
    exit /b 1
)

if not exist "%SRC_DIR%\packages" (
    echo Restoring packages using NuGet.config located at: %SRC_DIR%\NuGet.config ...
    echo If online: missing packages will be downloaded into the local packages folder.
    echo If offline: existing local packages will be used and failed remote sources will be ignored.
)

echo.
echo Restoring SafeLauncher packages...
dotnet restore "%PROJECT_DIR%\SafeLauncher.csproj" -r win-x64 --ignore-failed-sources --configfile "%SRC_DIR%\NuGet.config"

if errorlevel 1 (
	echo Restore failed due to packages could not be downloaded from the nuget repo, or local packages folder does not exist or missing packages.
	echo make sure the Artifactory url is valid and you have read access to the repo
	echo if running from an online pc - make sure the https://api.nuget.org/v3/index.json entry exists
	exit /b 1
)

echo.
echo Building SafeLauncher...
dotnet publish "%PROJECT_DIR%\SafeLauncher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:ApplicationIcon="%LAUNCHER_ICON_PATH%" --no-restore

if errorlevel 1 (
    echo.
    echo Build failed.
    exit /b 1
)

echo.
echo Copying launcher to dist package folder...
if not exist "%DIST_DIR%" mkdir "%DIST_DIR%"

if errorlevel 1 (
    echo Failed to create dist package folder: %DIST_DIR%.
    exit /b 1
)

if not exist "%PUBLISHED_EXE%" (
    echo Failed to build launcher: %PUBLISHED_EXE%, it does not exist.
    exit /b 1
)

copy /Y "%PUBLISHED_EXE%" "%DIST_EXE%" >NUL

if errorlevel 1 (
    echo Failed to copy launcher to %DIST_EXE%.
    exit /b 1
)

echo.
echo Build succeeded.
echo Runnable EXE:
echo %DIST_EXE%

exit /b 0
