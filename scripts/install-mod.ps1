#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the WorldBoxBridge mod (and BepInEx) into a WorldBox install on Windows.

.DESCRIPTION
    Two modes:
      * Default — downloads the latest BepInEx 5 Mono x64 and the latest
        WorldBoxBridge.dll from the GitHub release, installs both.
      * -Local — uses a locally built DLL at mod/src/WorldBoxBridge/bin/Release/net462/WorldBoxBridge.dll
        (for development).

    Generates a per-install auth token in BepInEx/config/WorldBoxBridge.cfg if not present.

.PARAMETER WorldBoxPath
    Path to the WorldBox install. If omitted, auto-discovered from common Steam libraries.

.PARAMETER Local
    Use the locally built DLL instead of downloading from the latest GitHub release.

.PARAMETER BepInExVersion
    Pin a specific BepInEx 5 release tag (e.g. "v5.4.23.2").

.EXAMPLE
    iex (irm https://raw.githubusercontent.com/fullya99/worldbox-mcp/main/scripts/install-mod.ps1)

.EXAMPLE
    .\scripts\install-mod.ps1 -Local
#>

[CmdletBinding()]
param(
    [string]$WorldBoxPath,
    [switch]$Local,
    [string]$BepInExVersion = 'v5.4.23.2'
)

$ErrorActionPreference = 'Stop'

# ─── Helpers ─────────────────────────────────────────────

function Find-WorldBoxPath {
    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\worldbox',
        'C:\Program Files\Steam\steamapps\common\worldbox'
    )
    # Scan all drive letters for SteamLibrary/steamapps/common/worldbox
    foreach ($drive in [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' }) {
        $candidates += "$($drive.Name)SteamLibrary\steamapps\common\worldbox"
        $candidates += "$($drive.Name)Steam\steamapps\common\worldbox"
        $candidates += "$($drive.Name)GAMES\steamapps\common\worldbox"
    }
    foreach ($p in $candidates | Select-Object -Unique) {
        if (Test-Path (Join-Path $p 'worldbox.exe')) {
            return (Resolve-Path $p).Path
        }
    }
    return $null
}

function Stop-WorldBoxIfRunning {
    $proc = Get-Process -Name 'worldbox' -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Warning "WorldBox is running (PID $($proc.Id)). The mod can't be installed while files are locked."
        $answer = Read-Host "Close it now? [y/N]"
        if ($answer -match '^[yY]') {
            $proc | Stop-Process -Force
            Start-Sleep -Seconds 2
        }
        else {
            throw "Aborting: please close WorldBox and re-run."
        }
    }
}

function Get-LatestRelease {
    param([string]$Repo)
    $uri = "https://api.github.com/repos/$Repo/releases/latest"
    $headers = @{ 'User-Agent' = 'worldbox-mcp-installer' }
    return Invoke-RestMethod -Uri $uri -Headers $headers
}

function Download-To {
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [Parameter(Mandatory)] [string]$Destination
    )
    Write-Host "→ Downloading $([System.IO.Path]::GetFileName($Destination))..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing
}

function Generate-Token {
    return -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 48 | ForEach-Object { [char]$_ })
}

# ─── Main ────────────────────────────────────────────────

Write-Host "worldbox-mcp installer" -ForegroundColor White
Write-Host "----------------------" -ForegroundColor DarkGray

if (-not $WorldBoxPath) {
    $WorldBoxPath = Find-WorldBoxPath
    if (-not $WorldBoxPath) {
        throw "WorldBox install not found. Pass -WorldBoxPath '<path>' explicitly."
    }
}
if (-not (Test-Path (Join-Path $WorldBoxPath 'worldbox.exe'))) {
    throw "worldbox.exe not found in $WorldBoxPath"
}
Write-Host "✓ WorldBox at $WorldBoxPath" -ForegroundColor Green

Stop-WorldBoxIfRunning

# Step 1 — BepInEx
$bepinexCore = Join-Path $WorldBoxPath 'BepInEx\core\BepInEx.dll'
if (Test-Path $bepinexCore) {
    Write-Host "✓ BepInEx already installed" -ForegroundColor Green
}
else {
    Write-Host "→ Installing BepInEx $BepInExVersion" -ForegroundColor Cyan
    $bepZip = Join-Path $env:TEMP "BepInEx-$BepInExVersion.zip"
    $bepUri = "https://github.com/BepInEx/BepInEx/releases/download/$BepInExVersion/BepInEx_win_x64_$($BepInExVersion.TrimStart('v')).zip"
    Download-To -Uri $bepUri -Destination $bepZip
    Write-Host "→ Extracting to $WorldBoxPath" -ForegroundColor Cyan
    Expand-Archive -Path $bepZip -DestinationPath $WorldBoxPath -Force
    Remove-Item $bepZip -Force
}

# Step 2 — WorldBoxBridge.dll
$pluginsDir = Join-Path $WorldBoxPath 'BepInEx\plugins'
New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
$dllDest = Join-Path $pluginsDir 'WorldBoxBridge.dll'

if ($Local) {
    $scriptDir = Split-Path -Parent $PSCommandPath
    $repoRoot = Split-Path -Parent $scriptDir
    $built = Join-Path $repoRoot 'mod\src\WorldBoxBridge\bin\Release\net462\WorldBoxBridge.dll'
    if (-not (Test-Path $built)) {
        throw "Local DLL not found: $built. Run 'dotnet build -c Release' in mod/ first."
    }
    Copy-Item $built $dllDest -Force
    Write-Host "✓ Installed local WorldBoxBridge.dll" -ForegroundColor Green
}
else {
    $release = Get-LatestRelease -Repo 'fullya99/worldbox-mcp'
    $asset = $release.assets | Where-Object { $_.name -match '^WorldBoxBridge-v.*\.zip$' } | Select-Object -First 1
    if (-not $asset) {
        throw "No WorldBoxBridge-*.zip asset on the latest release. Check https://github.com/fullya99/worldbox-mcp/releases"
    }
    $zip = Join-Path $env:TEMP $asset.name
    Download-To -Uri $asset.browser_download_url -Destination $zip
    $tmp = Join-Path $env:TEMP "WorldBoxBridge-stage"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $tmp
    Copy-Item -Recurse -Force (Join-Path $tmp 'WorldBoxBridge\WorldBoxBridge.dll') $dllDest
    Remove-Item $zip -Force
    Remove-Item $tmp -Recurse -Force
    Write-Host "✓ Installed WorldBoxBridge.dll from $($release.tag_name)" -ForegroundColor Green
}

# Step 3 — Config + token
$configDir = Join-Path $WorldBoxPath 'BepInEx\config'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$configFile = Join-Path $configDir 'WorldBoxBridge.cfg'
if (-not (Test-Path $configFile)) {
    $token = Generate-Token
    @"
## WorldBoxBridge configuration
## Generated $(Get-Date -Format o)

[Bridge]
enabled = true
host    = 127.0.0.1
port    = 8723
token   = $token
"@ | Set-Content -Path $configFile -Encoding UTF8
    Write-Host "✓ Generated $configFile with a fresh token" -ForegroundColor Green
}
else {
    Write-Host "✓ Existing $configFile preserved" -ForegroundColor Green
}

Write-Host ""
Write-Host "Install complete." -ForegroundColor White
Write-Host "Next:" -ForegroundColor White
Write-Host "  1. Enable Experimental Mode in WorldBox (Settings → Experimental Mode)." -ForegroundColor DarkGray
Write-Host "  2. Launch WorldBox. Check $WorldBoxPath\BepInEx\LogOutput.log for:" -ForegroundColor DarkGray
Write-Host "       [Info: WorldBoxBridge] listening on 127.0.0.1:8723" -ForegroundColor DarkGray
Write-Host "  3. Run .\scripts\verify-install.ps1 to confirm." -ForegroundColor DarkGray
