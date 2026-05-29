@echo off
setlocal

set "TOOLS_DIR=%~dp0"
set "PROJECT_DIR=%TOOLS_DIR%.."
for %%I in ("%PROJECT_DIR%") do set "PROJECT_DIR=%%~fI"
set "ROOT=%PROJECT_DIR%\..\.."
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "CONFIG_PATH=%PROJECT_DIR%\launcher-config.json"
set "GENERATED_CONFIG=%PROJECT_DIR%\Generated\LauncherConfig.g.cs"
set "LAUNCHER_OUTPUT_METADATA=%PROJECT_DIR%\Generated\LauncherOutput.cmd"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net8.0-windows\win-x64\publish"
set "PUBLISHED_EXE=%PUBLISH_DIR%\SafeLauncher.exe"

echo Generating embedded launcher config...
powershell -NoProfile -ExecutionPolicy Bypass -File "%TOOLS_DIR%generate-launcher-config.ps1" -ConfigPath "%CONFIG_PATH%" -OutputPath "%GENERATED_CONFIG%" -LauncherOutputMetadataPath "%LAUNCHER_OUTPUT_METADATA%"

if errorlevel 1 (
    echo.
    echo Config generation failed.
    exit /b 1
)

call "%LAUNCHER_OUTPUT_METADATA%"
set "DIST_DIR=%ROOT%\dist\%LAUNCHER_PACKAGE_NAME%"
set "DIST_EXE=%DIST_DIR%\%LAUNCHER_EXE_NAME%"

echo.
echo Building SafeLauncher...
if defined LAUNCHER_ICON_PATH (
    dotnet publish "%PROJECT_DIR%\SafeLauncher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:ApplicationIcon="%LAUNCHER_ICON_PATH%"
) else (
    dotnet publish "%PROJECT_DIR%\SafeLauncher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
)

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
