@echo off
setlocal

set "GEMINI_SETTINGS_DIR=C:\gemini-cli\.gemini"
set "GEMINI_SETTINGS_FILE=%GEMINI_SETTINGS_DIR%\settings.json"

if not exist "%GEMINI_SETTINGS_DIR%" (
    mkdir "%GEMINI_SETTINGS_DIR%"
)

if errorlevel 1 (
    exit /b 1
)

> "%GEMINI_SETTINGS_FILE%" (
    echo {
    echo   "general": {
    echo     "enableAutoUpdate": false,
    echo     "enableAutoUpdateNotification": false
    echo   },
    echo   "security": {
    echo     "auth": {
    echo       "selectedType": "gemini-api-key"
    echo     },
    echo     "folderTrust": {
    echo       "enabled": true
    echo     }
    echo   }
    echo }
)

if errorlevel 1 (
    exit /b 1
)

setx GEMINI_CLI_SYSTEM_SETTINGS_PATH "%GEMINI_SETTINGS_FILE%" >NUL

if errorlevel 1 (
    exit /b 1
)

exit /b 0
