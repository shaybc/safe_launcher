param(
    [Parameter(Mandatory = $true)]
    [string] $ConfigPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $false)]
    [string] $LauncherOutputMetadataPath
)

$ErrorActionPreference = "Stop"

function ConvertTo-CSharpStringLiteral {
    param([AllowNull()][string] $Value)

    if ($null -eq $Value) {
        return '""'
    }

    $escaped = $Value.Replace('\', '\\').Replace('"', '\"')
    return '"' + $escaped + '"'
}

function Resolve-ConfigRelativePath {
    param(
        [string] $BaseDirectory,
        [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BaseDirectory $Path))
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "Launcher config file was not found: $ConfigPath"
}

$resolvedConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)
$rootConfigDirectory = Split-Path -Parent $resolvedConfigPath
$rootConfig = Get-Content -LiteralPath $resolvedConfigPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace([string]$rootConfig.configPackageFolder)) {
    throw "Missing required root launcher config value: configPackageFolder"
}

$configPackageFolder = Resolve-ConfigRelativePath -BaseDirectory $rootConfigDirectory -Path ([string]$rootConfig.configPackageFolder)
if (-not (Test-Path -LiteralPath $configPackageFolder -PathType Container)) {
    throw "Config package folder was not found: $configPackageFolder"
}

$packageName = Split-Path -Leaf $configPackageFolder
if ([string]::IsNullOrWhiteSpace($packageName)) {
    throw "Selected config package folder does not have a valid folder name: $configPackageFolder"
}

$packageConfigPath = Join-Path $configPackageFolder "launcher-config.json"
if (-not (Test-Path -LiteralPath $packageConfigPath -PathType Leaf)) {
    throw "Package launcher config file was not found: $packageConfigPath"
}

$configDirectory = $configPackageFolder
$config = Get-Content -LiteralPath $packageConfigPath -Raw | ConvertFrom-Json

$launcherExecutableName = "SafeLauncher.exe"
if ($null -ne $config.launcher -and -not [string]::IsNullOrWhiteSpace([string]$config.launcher.executableName)) {
    $launcherExecutableName = ([string]$config.launcher.executableName).Trim()
}

if ($launcherExecutableName -ne [System.IO.Path]::GetFileName($launcherExecutableName)) {
    throw "launcher.executableName must be a file name only, not a path: $launcherExecutableName"
}

if ($launcherExecutableName -notmatch '^[A-Za-z0-9._ -]+\.exe$') {
    throw "launcher.executableName must end with .exe and may contain only letters, numbers, spaces, dots, underscores, and hyphens."
}

$hideLauncherConsole = $false
if ($null -ne $config.launcher -and $null -ne $config.launcher.hideConsole) {
    $hideLauncherConsole = [bool]$config.launcher.hideConsole
}

$launcherIconPath = ""
if ($null -ne $config.launcher -and -not [string]::IsNullOrWhiteSpace([string]$config.launcher.iconFile)) {
    $launcherIconPath = Resolve-ConfigRelativePath -BaseDirectory $configDirectory -Path ([string]$config.launcher.iconFile)
    if (-not (Test-Path -LiteralPath $launcherIconPath -PathType Leaf)) {
        throw "Launcher icon file was not found: $launcherIconPath"
    }

    if ([System.IO.Path]::GetExtension($launcherIconPath) -ine ".ico") {
        throw "launcher.iconFile must point to a .ico file."
    }
}

$requiredValues = @{
    "restrictedUser.domain" = $config.restrictedUser.domain
    "restrictedUser.userName" = $config.restrictedUser.userName
    "credentialManager.targetName" = $config.credentialManager.targetName
    "destination.executable" = $config.destination.executable
    "destination.workingDirectory" = $config.destination.workingDirectory
    "preLaunch.batchFile" = $config.preLaunch.batchFile
}

foreach ($entry in $requiredValues.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
        throw "Missing required launcher config value: $($entry.Key)"
    }
}

$timeoutSeconds = 30
if ($null -ne $config.preLaunch.timeoutSeconds) {
    $timeoutSeconds = [int]$config.preLaunch.timeoutSeconds
}

if ($timeoutSeconds -lt 1) {
    throw "preLaunch.timeoutSeconds must be at least 1."
}

$batchPath = Resolve-ConfigRelativePath -BaseDirectory $configDirectory -Path ([string]$config.preLaunch.batchFile)
if (-not (Test-Path -LiteralPath $batchPath -PathType Leaf)) {
    throw "Pre-launch batch file was not found: $batchPath"
}

$splashEnabled = $false
if ($null -ne $config.splash -and $null -ne $config.splash.enabled) {
    $splashEnabled = [bool]$config.splash.enabled
}

$splashMinimumSeconds = 0
if ($null -ne $config.splash -and $null -ne $config.splash.minimumSeconds) {
    $splashMinimumSeconds = [int]$config.splash.minimumSeconds
}

if ($splashMinimumSeconds -lt 0) {
    throw "splash.minimumSeconds cannot be negative."
}

$splashImageBase64 = ""
if ($splashEnabled) {
    if ($null -eq $config.splash -or [string]::IsNullOrWhiteSpace([string]$config.splash.imageFile)) {
        throw "splash.imageFile is required when splash.enabled is true."
    }

    $splashImagePath = Resolve-ConfigRelativePath -BaseDirectory $configDirectory -Path ([string]$config.splash.imageFile)
    if (-not (Test-Path -LiteralPath $splashImagePath -PathType Leaf)) {
        throw "Splash image file was not found: $splashImagePath"
    }

    if ([System.IO.Path]::GetExtension($splashImagePath) -ine ".png") {
        throw "splash.imageFile must point to a .png file."
    }

    $splashImageBase64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($splashImagePath))
}

$arguments = @()
if ($null -ne $config.destination.arguments) {
    $arguments = @($config.destination.arguments) | ForEach-Object {
        "        " + (ConvertTo-CSharpStringLiteral ([string]$_))
    }
}

$batchBase64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($batchPath))

$content = @"
// <auto-generated />
// Generated from the selected config package by src/SafeLauncher/tools/generate-launcher-config.ps1.
// Do not edit this file directly; update the package config and rerun src/SafeLauncher/tools/build.bat.

using System;

internal static class LauncherConfig
{
    public const string Domain = $(ConvertTo-CSharpStringLiteral ([string]$config.restrictedUser.domain));
    public const string UserName = $(ConvertTo-CSharpStringLiteral ([string]$config.restrictedUser.userName));
    public const string CredentialTargetName = $(ConvertTo-CSharpStringLiteral ([string]$config.credentialManager.targetName));
    public const string WorkingDirectory = $(ConvertTo-CSharpStringLiteral ([string]$config.destination.workingDirectory));
    public const string DestinationExecutable = $(ConvertTo-CSharpStringLiteral ([string]$config.destination.executable));
    public const string LauncherExecutableName = $(ConvertTo-CSharpStringLiteral $launcherExecutableName);
    public static readonly bool HideLauncherConsole = $($hideLauncherConsole.ToString().ToLowerInvariant());
    public const int PreLaunchTimeoutSeconds = $timeoutSeconds;
    public const string PreLaunchBatchBase64 = $(ConvertTo-CSharpStringLiteral $batchBase64);
    public const bool SplashEnabled = $($splashEnabled.ToString().ToLowerInvariant());
    public const int SplashMinimumSeconds = $splashMinimumSeconds;
    public const string SplashImageBase64 = $(ConvertTo-CSharpStringLiteral $splashImageBase64);

    public static readonly string[] Arguments =
    {
$($arguments -join ",`r`n")
    };

    public static readonly byte[] PreLaunchBatchBytes = Convert.FromBase64String(PreLaunchBatchBase64);
    public static readonly byte[] SplashImageBytes = Convert.FromBase64String(SplashImageBase64);
}
"@

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($LauncherOutputMetadataPath)) {
    $metadataDirectory = Split-Path -Parent $LauncherOutputMetadataPath
    New-Item -ItemType Directory -Force -Path $metadataDirectory | Out-Null
    Set-Content -LiteralPath $LauncherOutputMetadataPath -Value "@echo off`r`nset `"LAUNCHER_EXE_NAME=$launcherExecutableName`"`r`nset `"LAUNCHER_PACKAGE_NAME=$packageName`"`r`nset `"LAUNCHER_ICON_PATH=$launcherIconPath`"`r`n" -Encoding ASCII
}
