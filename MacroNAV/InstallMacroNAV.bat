@echo off
setlocal enabledelayedexpansion

echo ============================================================
echo  MacroNAV Installer
echo ============================================================
echo.

set "PLUGIN_DIR=%~dp0"
set "INSTALLED=0"

for %%Y in (2027 2026 2025 2024) do (
    set "NW_DIR=C:\Program Files\Autodesk\Navisworks Manage %%Y"
    if exist "!NW_DIR!\Autodesk.Navisworks.Api.dll" (
        set "PLUGINS_DIR=!NW_DIR!\Plugins\MacroNAV"
        echo Installing to Navisworks %%Y...
        if not exist "!PLUGINS_DIR!" mkdir "!PLUGINS_DIR!"
        xcopy /y /e "%PLUGIN_DIR%MacroNAV.dll"      "!PLUGINS_DIR!\"
        xcopy /y /e "%PLUGIN_DIR%MacroNAV.addin"    "!PLUGINS_DIR!\"
        xcopy /y /e "%PLUGIN_DIR%PackageContents.xml" "!PLUGINS_DIR!\"
        echo   Installed to: !PLUGINS_DIR!
        set "INSTALLED=1"
    )
)

if "%INSTALLED%"=="0" (
    echo ERROR: No supported Navisworks installation found (2024-2027).
    echo Please install Navisworks Manage and try again.
    pause
    exit /b 1
)

echo.
echo Installation complete! Restart Navisworks and open Add-Ins ^> MacroNAV.
pause
