@echo off
setlocal

set "TOOLS_DIR=%~dp0"
set "PROJECT_DIR=%TOOLS_DIR%.."
for %%I in ("%PROJECT_DIR%") do set "PROJECT_DIR=%%~fI"
set "ROOT=%PROJECT_DIR%\..\.."
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "LAUNCHER_OUTPUT_METADATA=%PROJECT_DIR%\Generated\LauncherOutput.cmd"

if exist "%LAUNCHER_OUTPUT_METADATA%" (
    call "%LAUNCHER_OUTPUT_METADATA%"
) else (
    set "LAUNCHER_EXE_NAME=SafeLauncher.exe"
    set "LAUNCHER_PACKAGE_NAME=default"
)

set "EXE=%ROOT%\dist\%LAUNCHER_PACKAGE_NAME%\%LAUNCHER_EXE_NAME%"

if not exist "%EXE%" (
    echo Launcher EXE was not found: %EXE%
    echo Run src\SafeLauncher\tools\build.bat first.
    exit /b 1
)

"%EXE%" --provision-credential
exit /b %errorlevel%
