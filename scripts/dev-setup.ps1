#Requires -Version 5.1
<#
.SYNOPSIS
    Installs developer prerequisites for worldbox-mcp on Windows.

.DESCRIPTION
    Installs .NET SDK 8 and uv via winget if they're not already present.
    Verifies the install of each tool before claiming success.

.EXAMPLE
    .\scripts\dev-setup.ps1
#>

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Test-Command {
    param([string]$Name)
    return [bool](Get-Command -Name $Name -ErrorAction SilentlyContinue)
}

function Install-Winget {
    param(
        [Parameter(Mandatory)] [string]$Id,
        [Parameter(Mandatory)] [string]$FriendlyName,
        [Parameter(Mandatory)] [string]$VerifyCommand
    )

    if ((Test-Command $VerifyCommand) -and -not $Force) {
        Write-Host "✓ $FriendlyName already installed ($VerifyCommand found)" -ForegroundColor Green
        return
    }

    if (-not (Test-Command 'winget')) {
        throw "winget not found. Install App Installer from the Microsoft Store and retry."
    }

    Write-Host "→ Installing $FriendlyName via winget ($Id)..." -ForegroundColor Cyan
    winget install --id $Id --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget install $Id failed with exit code $LASTEXITCODE"
    }

    $env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Test-Command $VerifyCommand)) {
        Write-Warning "$FriendlyName installed but '$VerifyCommand' is not yet on PATH. Restart your shell."
    }
    else {
        Write-Host "✓ $FriendlyName installed" -ForegroundColor Green
    }
}

Write-Host "worldbox-mcp dev setup" -ForegroundColor White
Write-Host "----------------------" -ForegroundColor DarkGray

Install-Winget -Id 'Microsoft.DotNet.SDK.8'  -FriendlyName '.NET SDK 8' -VerifyCommand 'dotnet'
Install-Winget -Id 'astral-sh.uv'            -FriendlyName 'uv'         -VerifyCommand 'uv'

Write-Host ""
Write-Host "Done. Next steps:" -ForegroundColor White
Write-Host "  cd mod    && dotnet restore && dotnet build" -ForegroundColor DarkGray
Write-Host "  cd server && uv sync --all-extras"            -ForegroundColor DarkGray
