#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs MacroNAV into Navisworks Manage 2025 and/or 2027.

.DESCRIPTION
    MacroNAV ships separate DLLs for NW2025 and NW2027 because their
    Clash Detective APIs differ. This script copies each version's DLL
    into the matching Navisworks Plugins folder.

    Run from the project root after building both Release-NW2025 and
    Release-NW2027 configurations.
#>

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$Installed  = $false

function Install-ForVersion {
    param([string]$Year)

    $NwDir  = "C:\Program Files\Autodesk\Navisworks Manage $Year"
    $ApiDll = Join-Path $NwDir "Autodesk.Navisworks.Api.dll"

    if (-not (Test-Path $ApiDll)) {
        Write-Host "[NW$Year] Not found — skipping." -ForegroundColor DarkGray
        return
    }

    $SrcDir = Join-Path $ScriptDir "bin\Release-NW$Year"
    $DstDir = Join-Path $NwDir     "Plugins\MacroNAV"

    Write-Host "[NW$Year] Installing..." -ForegroundColor Cyan
    Write-Host "  Source : $SrcDir"
    Write-Host "  Target : $DstDir"

    New-Item -ItemType Directory -Force -Path $DstDir | Out-Null

    $DllSrc = Join-Path $SrcDir "MacroNAV.dll"
    if (-not (Test-Path $DllSrc)) {
        Write-Host "  [NW$Year] ERROR: MacroNAV.dll not found at $DllSrc" `
                   -ForegroundColor Red
        Write-Host "  Build with: msbuild MacroNAV.csproj /p:Configuration=Release-NW$Year /p:Platform=x64"
        return
    }

    Copy-Item -Force $DllSrc                                   $DstDir
    Copy-Item -Force (Join-Path $ScriptDir "MacroNAV.addin")   $DstDir
    Copy-Item -Force (Join-Path $ScriptDir "PackageContents.xml") $DstDir

    $IcoSrc = Join-Path $ScriptDir "MacroNAV.ico"
    if (Test-Path $IcoSrc) { Copy-Item -Force $IcoSrc $DstDir }

    Write-Host "  [NW$Year] OK" -ForegroundColor Green
    Set-Variable -Name Installed -Value $true -Scope 1
}

Write-Host ""
Write-Host "MacroNAV Installer  (NW2025 / NW2027)" -ForegroundColor White
Write-Host ("=" * 45)
Write-Host ""

Install-ForVersion -Year "2025"
Write-Host ""
Install-ForVersion -Year "2027"
Write-Host ""

if (-not $Installed) {
    Write-Host "ERROR: Neither Navisworks 2025 nor 2027 found." -ForegroundColor Red
    exit 1
}

Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "Restart Navisworks and open Add-Ins > MacroNAV."
Write-Host ""
Write-Host "Macros are stored at:  $env:APPDATA\MacroNAV\macros.json"
Write-Host "(shared between NW2025 and NW2027 installations)"
