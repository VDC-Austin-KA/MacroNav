# Launches Navisworks out-of-process via the Automation API and invokes the
# MacroNAV round-trip harness plugin, then prints its report.
$ErrorActionPreference = "Stop"

$nw  = "C:\Program Files\Autodesk\Navisworks Manage 2025"
$log = Join-Path $env:TEMP "macronav-roundtrip.log"
if (Test-Path $log) { Remove-Item -Force $log }
$env:MACRONAV_TEST_LOG = $log

# Deploy the harness (and the MacroNAV build it exercises) into the Navisworks
# plugin folder. Removed again in the finally block so a test plugin is never
# left registered in a real install.
$harnessDir = Join-Path $nw "Plugins\MacroNAVTests"
$binDir     = Join-Path $PSScriptRoot "bin"
$macroNavDll= Join-Path $PSScriptRoot "..\..\MacroNAV\bin\Release-NW2025\MacroNAV.dll"

New-Item -ItemType Directory -Force -Path $harnessDir | Out-Null
Copy-Item -Force (Join-Path $binDir "MacroNAVTests.dll") $harnessDir
Copy-Item -Force $macroNavDll $harnessDir

Add-Type -Path "$nw\Autodesk.Navisworks.Automation.dll"

$app = $null
try {
    $app = New-Object Autodesk.Navisworks.Api.Automation.NavisworksApplication
    $app.DisableProgress()
    Write-Host "Navisworks automation instance started." -ForegroundColor Cyan

    $rc = $app.ExecuteAddInPlugin("MacroNAVTest.ACLP_VDC")
    Write-Host "ExecuteAddInPlugin returned: $rc"
}
finally {
    if ($app) { try { $app.Dispose() } catch {} }

    # Dispose returns before Roamer.exe has fully exited, so the harness DLL is
    # still locked for a moment. Wait for the process to go, then retry the
    # delete -- otherwise a test plugin stays registered in a real install.
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Process -Name Roamer -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    $removed = $false
    while (-not $removed -and (Get-Date) -lt $deadline) {
        try {
            Remove-Item -Recurse -Force $harnessDir -ErrorAction Stop
            $removed = $true
        }
        catch {
            if (-not (Test-Path $harnessDir)) { $removed = $true; break }
            Start-Sleep -Milliseconds 500
        }
    }
    if ($removed) {
        Write-Host "Harness plugin removed from Navisworks." -ForegroundColor DarkGray
    } else {
        Write-Host "WARNING: could not remove $harnessDir (still locked). Delete it manually." -ForegroundColor Red
    }
}

Write-Host ""
if (Test-Path $log) {
    Write-Host "===== HARNESS REPORT =====" -ForegroundColor Yellow
    Get-Content $log
} else {
    Write-Host "No report written to $log" -ForegroundColor Red
}
