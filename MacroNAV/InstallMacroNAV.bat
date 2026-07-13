@echo off
setlocal enabledelayedexpansion

echo ============================================================
echo  MacroNAV Installer  (Navisworks 2025 and 2027)
echo ============================================================
echo.
echo MacroNAV ships two separate DLLs because the Clash Detective
echo API changed significantly between NW2025 and NW2027.
echo.

set "SCRIPT_DIR=%~dp0"
set "INSTALLED=0"

:: ── Navisworks 2025 ──────────────────────────────────────────────────────────
set "NW2025_DIR=C:\Program Files\Autodesk\Navisworks Manage 2025"
if exist "!NW2025_DIR!\Autodesk.Navisworks.Api.dll" (
    set "SRC=%SCRIPT_DIR%bin\Release-NW2025\"
    set "DST=!NW2025_DIR!\Plugins\MacroNAV\"
    if not exist "!DST!" mkdir "!DST!"
    echo [NW2025] Copying from: !SRC!
    echo [NW2025] Installing to: !DST!
    xcopy /y "!SRC!MacroNAV.dll"        "!DST!"
    xcopy /y "%SCRIPT_DIR%MacroNAV.addin"      "!DST!"
    xcopy /y "%SCRIPT_DIR%PackageContents.xml" "!DST!"
    echo [NW2025] Done.
    set "INSTALLED=1"
) else (
    echo [NW2025] Not found - skipping.
)

echo.

:: ── Navisworks 2027 ──────────────────────────────────────────────────────────
set "NW2027_DIR=C:\Program Files\Autodesk\Navisworks Manage 2027"
if exist "!NW2027_DIR!\Autodesk.Navisworks.Api.dll" (
    set "SRC=%SCRIPT_DIR%bin\Release-NW2027\"
    set "DST=!NW2027_DIR!\Plugins\MacroNAV\"
    if not exist "!DST!" mkdir "!DST!"
    echo [NW2027] Copying from: !SRC!
    echo [NW2027] Installing to: !DST!
    xcopy /y "!SRC!MacroNAV.dll"        "!DST!"
    xcopy /y "%SCRIPT_DIR%MacroNAV.addin"      "!DST!"
    xcopy /y "%SCRIPT_DIR%PackageContents.xml" "!DST!"
    echo [NW2027] Done.
    set "INSTALLED=1"
) else (
    echo [NW2027] Not found - skipping.
)

echo.
if "%INSTALLED%"=="0" (
    echo ERROR: Neither Navisworks 2025 nor 2027 was found on this machine.
    echo Please install Navisworks Manage 2025 or 2027 and try again.
    pause
    exit /b 1
)

echo Installation complete!
echo Restart Navisworks and open the Add-Ins tab to launch MacroNAV.
echo.
echo NOTE: Macros are stored in:
echo   %%APPDATA%%\MacroNAV\macros.json
echo This file is shared between both NW2025 and NW2027 installations.
pause
