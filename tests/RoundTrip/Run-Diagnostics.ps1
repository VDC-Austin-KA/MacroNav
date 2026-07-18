# Runs the MacroNAV recorder auto-capture diagnostics inside a real Navisworks
# instance and prints the report.
$ErrorActionPreference = "Stop"

$nw  = "C:\Program Files\Autodesk\Navisworks Manage 2025"
$log = Join-Path $env:TEMP "macronav-diag.log"
if (Test-Path $log) { Remove-Item -Force $log }
$env:MACRONAV_DIAG_LOG = $log

$harnessDir  = Join-Path $nw "Plugins\MacroNAVTests"
$binDir      = Join-Path $PSScriptRoot "bin"
$macroNavDll = Join-Path $PSScriptRoot "..\..\MacroNAV\bin\Release-NW2025\MacroNAV.dll"

New-Item -ItemType Directory -Force -Path $harnessDir | Out-Null
Copy-Item -Force (Join-Path $binDir "MacroNAVTests.dll") $harnessDir
Copy-Item -Force $macroNavDll $harnessDir

Add-Type -Path "$nw\Autodesk.Navisworks.Automation.dll"

$app = $null
try {
    $app = New-Object Autodesk.Navisworks.Api.Automation.NavisworksApplication
    $app.DisableProgress()
    $rc = $app.ExecuteAddInPlugin("MacroNAVDiag.ACLP_VDC")
    Write-Host "ExecuteAddInPlugin returned: $rc"
}
finally {
    if ($app) { try { $app.Dispose() } catch {} }
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Process -Name Roamer -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    $removed = $false
    while (-not $removed -and (Get-Date) -lt $deadline) {
        try { Remove-Item -Recurse -Force $harnessDir -ErrorAction Stop; $removed = $true }
        catch {
            if (-not (Test-Path $harnessDir)) { $removed = $true; break }
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $removed) { Write-Host "WARNING: could not remove $harnessDir" -ForegroundColor Red }
}

Write-Host ""
if (Test-Path $log) { Get-Content $log } else { Write-Host "No report written." -ForegroundColor Red }
