@echo off
setlocal

set "TOOLS_DIR=%~dp0"
set "PROJECT_DIR=%TOOLS_DIR%.."

echo Cleaning previous build outputs...

if exist "%PROJECT_DIR%\Generated" rmdir /S /Q "%PROJECT_DIR%\Generated"
if exist "%PROJECT_DIR%\bin" rmdir /S /Q "%PROJECT_DIR%\bin"
if exist "%PROJECT_DIR%\obj" rmdir /S /Q "%PROJECT_DIR%\obj"

echo Clean completed.
echo .
