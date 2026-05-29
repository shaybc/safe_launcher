# Safe Launcher

## Intro

Safe Launcher is a controlled Windows launcher prototype for running one approved app under a restricted local user account instead of the developer's normal account.

The current Phase 2 app is a local C# EXE that:

- Reads a restricted-user password from Windows Credential Manager.
- Logs on as that restricted user.
- Runs an admin-authored pre-launch batch as that restricted user.
- Creates a fresh restricted-user environment after the batch.
- Starts one fixed destination app with `CreateProcessWithLogonW`.

Runtime users cannot choose executable paths, arguments, working directories, environment variables, or pre-launch actions. Those values are compiled into `SafeLauncher.exe` from the selected config package during build.

This is useful when you want to run AI tools or other automation with lower Windows privileges, keep file access focused on a dedicated workspace, and prove the launch flow before building a service broker.

## Install

Install prerequisites:

- Windows 10 or Windows 11.
- .NET SDK 8.0 or newer for building from source.
- A restricted local Windows user, for example `ai_agent_user`.
- A dedicated workspace, for example `C:\ai-workspace`.
- The destination app installed in a fixed location.
- A provisioned Credential Manager entry for the restricted user.

Create the workspace:

```powershell
New-Item -ItemType Directory -Force -Path C:\ai-workspace
```

Create a restricted local user:

```powershell
net user ai_agent_user * /add
net localgroup Users ai_agent_user /add
net localgroup Administrators ai_agent_user /delete
```

Grant access to the workspace:

```powershell
icacls C:\ai-workspace /inheritance:r
icacls C:\ai-workspace /grant ai_agent_user:(OI)(CI)M
icacls C:\ai-workspace /grant Administrators:(OI)(CI)F
icacls C:\ai-workspace /grant SYSTEM:(OI)(CI)F
```

For real hardening, use Windows policy controls such as AppLocker, WDAC, Local Security Policy, or Group Policy. File permissions alone are not enough to block all system tools.

Recommended policy restrictions for `ai_agent_user`:

- Keep the user out of administrator, remote access, and service-management groups.
- Block unapproved shells, browsers, package managers, scripting hosts, service tools, and network utilities.
- Allow only signed or hash-pinned approved binaries.
- Deny interactive logon if the final design does not require it.
- Ensure the user cannot access developer profile folders or unrelated project folders.

## Build-Time Config

The root config only selects which config package to build:

```text
C:\GitHub\user_name\safe_launcher\src\SafeLauncher\launcher-config.json
```

Example root config:

```json
{
  "configPackageFolder": "C:\\GitHub\\user_name\\safe_launcher\\config-packages\\gemini-cli"
}
```

The selected package folder contains:

```text
config-packages\gemini-cli\launcher-config.json
config-packages\gemini-cli\gemini-prelaunch.cmd
```

The package `launcher-config.json` contains the actual launcher settings:

```json
{
  "launcher": {
    "executableName": "gcli.exe",
    "iconFile": "gcli.ico"
  },
  "restrictedUser": {
    "domain": ".",
    "userName": "ai_agent_user"
  },
  "credentialManager": {
    "targetName": "SafeLauncher/ai_agent_user"
  },
  "destination": {
    "executable": "node.exe",
    "workingDirectory": "C:\\ai-workspace",
    "arguments": [
      "C:\\gemini-cli\\node_modules\\@google\\gemini-cli\\bundle\\gemini.js",
      "--approval-mode",
      "default",
      "-e",
      "none",
      "--policy",
      "C:\\gemini-cli\\.gemini\\policies\\deny-shell.toml"
    ]
  },
  "preLaunch": {
    "batchFile": "gemini-prelaunch.cmd",
    "timeoutSeconds": 30
  },
  "splash": {
    "enabled": false,
    "imageFile": "splash.png",
    "minimumSeconds": 2
  }
}
```

Changing either config file has no effect on an already-built EXE. Re-run `src\SafeLauncher\tools\build.bat` to embed the selected package values.

`launcher.executableName` controls the runnable EXE copied under `dist\<config-package-folder>\` after build. It must be a file name only, not a path, and it must end with `.exe`. This lets an administrator build separate launchers such as:

```text
gcli.exe
SafeLauncher-ADK.exe
SafeLauncher-CrewAI.exe
```

Use stable admin-chosen names and avoid names that mimic the destination executable itself, such as `node.exe`, `python.exe`, or tool names that trigger app-specific restart bugs.

`launcher.iconFile` is optional. When set, it must point to a `.ico` file, usually copied into the config package folder by the admin UI. The build script applies it as the generated EXE's Windows icon. Leaving it empty or omitting it keeps the default application icon and does not break the build.

## Splash Screen

Each config package can optionally embed a PNG splash screen into the generated launcher:

- `splash.enabled`: `true` shows the splash screen when the launcher starts.
- `splash.imageFile`: PNG path to embed at build time. Relative paths are resolved from the config package folder; absolute paths can point anywhere the system admin can read during build.
- `splash.minimumSeconds`: minimum number of seconds the splash screen remains visible.

The admin UI includes these fields and a file picker for selecting the PNG. The image is embedded into the built EXE, so the PNG does not need to be present on the user workstation after deployment.

## Pre-Launch Batch

`preLaunch.batchFile` points to an admin-authored batch file inside the selected config package. The generator reads that file during build and embeds it into `SafeLauncher.exe`. The batch file is not required at runtime.

The embedded batch runs before every destination launch:

- It runs as the restricted user.
- It runs hidden, with no visible CMD window.
- It uses the configured destination working directory.
- It must finish within `preLaunch.timeoutSeconds`.
- If it exits non-zero, times out, or cannot start, the destination app is not launched.

The batch may use normal Windows commands such as `copy`, `del`, `mkdir`, redirects, and `setx`. Because it runs as the restricted user, writes only succeed where that user has permission.

Important environment behavior:

- `set` affects only the batch process and will not affect the destination app.
- `setx` persists values to the restricted user's environment.
- Safe Launcher creates a fresh restricted-user environment after the batch completes, so `setx` changes can affect the destination app.

Do not put secrets in the batch. Embedded content is not runtime-editable, but a determined local administrator or reverse engineer should be treated as able to extract it.

The included Gemini example batch creates `C:\gemini-cli\.gemini\settings.json` and persists `GEMINI_CLI_SYSTEM_SETTINGS_PATH` for `ai_agent_user`.

## Credential Storage

The restricted user's password is stored as a Windows Credential Manager Generic Credential.

After building, provision the credential:

```bat
src\SafeLauncher\tools\provision-credential.bat
```

Or run the EXE directly:

```bat
SafeLauncher.exe --provision-credential
```

`src\SafeLauncher\tools\provision-credential.bat` runs the compiled launcher with the `--provision-credential` management command. It prompts the operator for the password of the configured restricted account, then saves that password into Windows Credential Manager under `credentialManager.targetName`.

Provisioning must run on the workstation where `SafeLauncher.exe` will be used, under the same Windows account that will later run `SafeLauncher.exe`. If an administrator provisions it from a different workstation or a different Windows account, the normal user will not see that credential.

Run provisioning when:

- `SafeLauncher.exe` is first installed for that Windows user.
- The restricted user's password changes.
- `credentialManager.targetName` changes and the app is rebuilt.
- The stored Credential Manager entry is deleted or corrupted.

To verify the credential exists, open:

```text
Control Panel -> Credential Manager -> Windows Credentials
```

Look under Generic Credentials for the configured target, for example:

```text
SafeLauncher/ai_agent_user
```

You can also check from CMD:

```bat
cmdkey /list
```

After provisioning on an end-user workstation, an administrator should remove the provisioning helper from that workstation if it is not needed there:

```text
Shift+Delete src\SafeLauncher\tools\provision-credential.bat
```

Deleting the batch file does not delete the stored credential. It only removes the helper that writes or overwrites it.

Important security note: Credential Manager protects the password from ordinary casual access, but a local administrator on the workstation should be treated as able to access or extract local secrets. If the end user is an administrator on the workstation, assume they may be able to access the stored password or replace/debug the launcher.

## Startup Checks

Before reading the stored password and launching the destination, Safe Launcher performs generic preflight checks:

- Windows 10 or Windows 11 is running.
- The configured destination working directory exists.
- The configured destination executable resolves to a full path and exists.
- The configured Credential Manager entry exists.
- The restricted local user exists.
- The restricted local user is not in the local `Administrators` group.
- .NET SDK 8.0 or newer is installed for compiling from source.

The .NET SDK check is reported as a compile prerequisite. A published EXE can still run without the SDK.

App-specific checks are intentionally not in Safe Launcher. Put those checks in the embedded pre-launch batch or in IT deployment validation.

## Compile

From this repository root:

```bat
src\SafeLauncher\tools\build.bat
```

The build script:

- Reads the root `launcher-config.json`.
- Loads the selected package folder.
- Reads the package `launcher-config.json`.
- Reads and embeds the package-relative `preLaunch.batchFile`.
- Runs `src\SafeLauncher\tools\generate-launcher-config.ps1`.
- Generates `src\SafeLauncher\Generated\LauncherConfig.g.cs`.
- Publishes a self-contained `win-x64` EXE.
- Copies the runnable EXE to `dist\<config-package-folder>\` using `launcher.executableName`.

With the sample config, the runnable EXE is copied here:

```text
C:\GitHub\user_name\safe_launcher\dist\gemini-cli\gcli.exe
```

The published EXE is also available here:

```text
C:\GitHub\user_name\safe_launcher\src\SafeLauncher\bin\Release\net8.0-windows\win-x64\publish\SafeLauncher.exe
```

## Admin UI

SafeLauncher includes a Windows admin UI for managing config packages:

```text
C:\GitHub\user_name\safe_launcher\src\SafeLauncherAdmin
```

Build it with:

```bat
dotnet build C:\GitHub\user_name\safe_launcher\src\SafeLauncherAdmin\SafeLauncherAdmin.csproj -c Release
```

Run:

```text
C:\GitHub\user_name\safe_launcher\src\SafeLauncherAdmin\bin\Release\net8.0-windows\SafeLauncherAdmin.exe
```

Or use the admin UI build helper:

```bat
C:\GitHub\user_name\safe_launcher\src\SafeLauncherAdmin\build.bat
```

That publishes the UI and copies it to:

```text
C:\GitHub\user_name\safe_launcher\safe_launcher_admin.exe
```

You can also publish manually:

```bat
dotnet publish C:\GitHub\user_name\safe_launcher\src\SafeLauncherAdmin\SafeLauncherAdmin.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Published EXE:

```text
C:\GitHub\user_name\safe_launcher\src\SafeLauncherAdmin\bin\Release\net8.0-windows\win-x64\publish\SafeLauncherAdmin.exe
```

The UI lets an administrator:

- Create new folders under `config-packages`.
- Edit an existing package `launcher-config.json`.
- Edit the package pre-launch batch.
- Select the active package by updating the root `launcher-config.json`.
- Build the selected package.
- Open credential provisioning for the selected package.

The Provision Credential button opens a visible console because the credential command prompts for the restricted user's password.

## Usage

Run the launcher:

```powershell
C:\GitHub\user_name\safe_launcher\dist\gemini-cli\gcli.exe
```

The launcher accepts no normal runtime arguments. It reads the stored Credential Manager password, runs the embedded pre-launch batch as the restricted user, creates a fresh environment, and starts the configured destination app.

Example controlled Windows app launch config:

```json
{
  "destination": {
    "executable": "C:\\Windows\\System32\\notepad.exe",
    "workingDirectory": "C:\\ai-workspace",
    "arguments": [
      "C:\\ai-workspace\\notes.txt"
    ]
  }
}
```

This example launches Notepad as the restricted user and sets the default working area to `C:\ai-workspace`. In a real allowlisted launcher, keep the application path and arguments in IT-controlled build-time configuration. Do not accept user-supplied executable paths, arguments, environment variables, or working directories.

For production, move from this Phase 2 EXE to:

```text
User app -> named pipe or localhost API -> Windows service broker -> approved launch only
```

The service should own credentials, validate predefined actions, create a clean environment, launch approved tools only, and log every launch.
