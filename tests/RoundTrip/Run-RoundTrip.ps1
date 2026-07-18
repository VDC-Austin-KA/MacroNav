# Launches Navisworks out-of-process via the Automation API and invokes the
# MacroNAV round-trip harness plugin, then prints its report.
$ErrorActionPreference = "Stop"

$nw  = "C:\Program Files\Autodesk\Navisworks Manage 2025"
$log = Join-Path $env:TEMP "macronav-roundtrip.log"
if (Test-Path $log) { Remove-Item -Force $log }
$env:MACRONAV_TEST_LOG = $log

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
}

Write-Host ""
if (Test-Path $log) {
    Write-Host "===== HARNESS REPORT =====" -ForegroundColor Yellow
    Get-Content $log
} else {
    Write-Host "No report written to $log" -ForegroundColor Red
}
