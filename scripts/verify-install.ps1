#Requires -Version 5.1
<#
.SYNOPSIS
    Verifies a worldbox-mcp install is healthy.

.DESCRIPTION
    Checks (in order):
      1. WorldBox is installed and discoverable.
      2. BepInEx is present.
      3. WorldBoxBridge.dll is in BepInEx/plugins/.
      4. WorldBoxBridge.cfg exists and has a token.
      5. The HTTP /health endpoint responds with ok:true (requires WorldBox running).
#>

[CmdletBinding()]
param([string]$WorldBoxPath)

$ErrorActionPreference = 'Stop'

function Find-WorldBoxPath {
    $candidates = @()
    foreach ($drive in [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' }) {
        $candidates += "$($drive.Name)SteamLibrary\steamapps\common\worldbox"
        $candidates += "$($drive.Name)Steam\steamapps\common\worldbox"
        $candidates += "$($drive.Name)GAMES\steamapps\common\worldbox"
        $candidates += "$($drive.Name)Program Files (x86)\Steam\steamapps\common\worldbox"
    }
    foreach ($p in $candidates | Select-Object -Unique) {
        if (Test-Path (Join-Path $p 'worldbox.exe')) { return (Resolve-Path $p).Path }
    }
    return $null
}

function Check {
    param(
        [Parameter(Mandatory)] [string]$Label,
        [Parameter(Mandatory)] [scriptblock]$Test
    )
    Write-Host -NoNewline "  $Label ... " -ForegroundColor DarkGray
    try {
        $result = & $Test
        if ($result) {
            Write-Host "OK" -ForegroundColor Green
            return $true
        }
        Write-Host "MISSING" -ForegroundColor Yellow
        return $false
    }
    catch {
        Write-Host "FAIL ($($_.Exception.Message))" -ForegroundColor Red
        return $false
    }
}

if (-not $WorldBoxPath) { $WorldBoxPath = Find-WorldBoxPath }

Write-Host "worldbox-mcp install check" -ForegroundColor White
Write-Host "---------------------------" -ForegroundColor DarkGray
if (-not $WorldBoxPath) {
    Write-Host "WorldBox install not found." -ForegroundColor Red
    exit 1
}
Write-Host "  WorldBox path: $WorldBoxPath" -ForegroundColor DarkGray
Write-Host ""

$ok = $true
$ok = (Check 'WorldBox executable' { Test-Path (Join-Path $WorldBoxPath 'worldbox.exe') }) -and $ok
$ok = (Check 'BepInEx core'        { Test-Path (Join-Path $WorldBoxPath 'BepInEx\core\BepInEx.dll') }) -and $ok
$ok = (Check 'WorldBoxBridge.dll'  { Test-Path (Join-Path $WorldBoxPath 'BepInEx\plugins\WorldBoxBridge.dll') }) -and $ok

$cfg = Join-Path $WorldBoxPath 'BepInEx\config\WorldBoxBridge.cfg'
$ok = (Check 'WorldBoxBridge.cfg'  { Test-Path $cfg }) -and $ok

$token = $null
$port = 8723
if (Test-Path $cfg) {
    $tokenLine = Select-String -Path $cfg -Pattern '^\s*token\s*=\s*(\S+)' | Select-Object -First 1
    if ($tokenLine) { $token = $tokenLine.Matches[0].Groups[1].Value }
    $portLine = Select-String -Path $cfg -Pattern '^\s*port\s*=\s*(\d+)' | Select-Object -First 1
    if ($portLine) { $port = [int]$portLine.Matches[0].Groups[1].Value }
}

$ok = (Check 'config contains token' { -not [string]::IsNullOrWhiteSpace($token) }) -and $ok

Write-Host ""
Write-Host "Live HTTP check (requires WorldBox running):" -ForegroundColor White
if (-not $token) {
    Write-Host "  Skipped, no token available." -ForegroundColor Yellow
}
else {
    try {
        $resp = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health" -Headers @{ 'X-WB-Token' = $token } -TimeoutSec 3
        if ($resp.ok) {
            Write-Host "  /health OK, mod_version=$($resp.mod_version), tick=$($resp.tick), unity=$($resp.unity_version)" -ForegroundColor Green
        }
        else {
            Write-Host "  /health returned ok=false: $($resp | ConvertTo-Json -Compress)" -ForegroundColor Yellow
            $ok = $false
        }
    }
    catch {
        Write-Host "  /health unreachable: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "    Make sure WorldBox is running with Experimental Mode enabled." -ForegroundColor DarkGray
    }
}

Write-Host ""
if ($ok) {
    Write-Host "All required components present." -ForegroundColor Green
    exit 0
}
else {
    Write-Host "Some components are missing, see above." -ForegroundColor Yellow
    exit 1
}
