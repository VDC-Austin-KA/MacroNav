#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs MacroNAV into all detected Navisworks Manage installations.
#>

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Installed = $false

foreach ($Year in @(2027, 2026, 2025, 2024)) {
    $NwDir = "C:\Program Files\Autodesk\Navisworks Manage $Year"
    if (Test-Path "$NwDir\Autodesk.Navisworks.Api.dll") {
        $PluginsDir = "$NwDir\Plugins\MacroNAV"
        Write-Host "Installing to Navisworks $Year..." -ForegroundColor Cyan
        New-Item -ItemType Directory -Force -Path $PluginsDir | Out-Null
        Copy-Item -Force "$ScriptDir\MacroNAV.dll"          $PluginsDir
        Copy-Item -Force "$ScriptDir\MacroNAV.addin"        $PluginsDir
        Copy-Item -Force "$ScriptDir\PackageContents.xml"   $PluginsDir
        Write-Host "  Installed to: $PluginsDir" -ForegroundColor Green
        $Installed = $true
    }
}

if (-not $Installed) {
    Write-Host "ERROR: No supported Navisworks installation found (2024-2027)." -ForegroundColor Red
    exit 1
}

Write-Host ""`nInstallation complete! Restart Navisworks and open Add-Ins > MacroNAV." -ForegroundColor Green
