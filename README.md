# MacroNAV

A macro recorder and playback plugin for Autodesk Navisworks Manage (2024–2027).

MacroNAV lets you record, edit, save, and replay sequences of Navisworks actions — including Clash Detective workflows, viewpoint navigation, selection sets, and AutoNAV operations — as named, reusable macros.

---

## Features

| Feature | Description |
|---|---|
| **Macro Library** | Save unlimited named macros, persisted across sessions in `%AppData%\MacroNAV\macros.json` |
| **Step Recording** | Click **Record** then use the **Quick Capture** panel to insert steps as you work |
| **Step Editor** | Double-click any step to edit its type, display name, and parameters |
| **Insert / Reorder** | Insert a new step above any existing step, drag-reorder with Up/Down buttons |
| **Enable / Disable** | Toggle individual steps off without deleting them |
| **Playback** | Click **Play Macro** to replay all enabled steps in sequence via the Navisworks API |
| **Import / Export** | Share macros as `.json` files |

---

## Supported Step Types

### Clash Detective
- `ClashCreateTest` — Create or reconfigure a clash test (name, Selection A, Selection B, tolerance, type)
- `ClashRunTest` — Run a named clash test
- `ClashRunAllTests` — Run all clash tests in the document
- `ClashAssignStatus` — Assign a result status (placeholder; extend as needed)

### Search / Selection Sets
- `SearchSetActivate` — Activate a named selection set as the current selection
- `SearchSetCreate` / `SearchSetDelete` — Placeholder; extend with your search criteria

### Viewpoints / Navigation
- `ViewpointActivate` — Restore a saved viewpoint by name, or restore a captured camera position (X/Y/Z + direction)
- `ViewpointSaveCurrent` — Save the current camera as a named viewpoint

### AutoNAV Integration
- `AutoNavSearchSetGen` — Documents an AutoNAV search-set-generation run (discipline + method); requires opening AutoNAV to execute
- `AutoNavClashTestGen` — Documents an AutoNAV clash-test-generation run

### Control
- `Comment` — Non-executing annotation step
- `Delay` — Wait N milliseconds between steps

---

## Quick Start

1. **Build** the plugin (see below) and install with `InstallMacroNAV.bat`.
2. Open Navisworks → **Add-Ins** tab → click **MacroNAV**.
3. Click **+ New Macro** in the left panel.
4. Click **⏺ Record** to start a recording session.
5. Use the **Quick Capture** panel on the right to capture steps:
   - Select a clash test from the dropdown, then click **Capture Test Config** or **Capture Run Test**.
   - Click **Capture Current View** to snapshot the current camera.
   - Add **Comments** or **Delays** between steps.
6. Click **■ Stop** when done.
7. Click **▶ Play Macro** to replay. Use **Edit**, **Move Up/Down**, or **Delete** to refine the steps.
8. Click **Export** to save the macro as a `.json` file to share with teammates.

---

## Building

**Requirements:**
- Visual Studio 2019+ or MSBuild 16+
- .NET Framework 4.8
- Autodesk Navisworks Manage 2024, 2025, 2026, or 2027 installed

```bat
:: Auto-detect newest installed Navisworks
msbuild MacroNAV\MacroNAV.csproj /p:Configuration=Release /p:Platform=x64

:: Or pin to a specific version
msbuild MacroNAV\MacroNAV.csproj /p:Configuration=Release-NW2025 /p:Platform=x64
```

Then run `InstallMacroNAV.bat` (or `InstallMacroNAV.ps1` as Administrator) from the output folder.

---

## Architecture

```
MacroNAV/
├── PluginMain.cs              ← Navisworks AddIn entry point
├── Models/
│   ├── MacroStep.cs           ← Step data model + enum of step types
│   └── Macro.cs               ← Collection of steps + metadata
├── MacroLibrary.cs            ← JSON persistence (%AppData%\MacroNAV)
├── MacroRecorder.cs           ← Captures steps; hooks Navisworks events
├── MacroPlayer.cs             ← Executes steps via Navisworks API
├── MacroRecorderWindow.xaml   ← Main UI (library + editor + capture panel)
├── MacroRecorderWindow.xaml.cs
├── StepEditorDialog.xaml      ← Edit/insert individual steps
├── StepEditorDialog.xaml.cs
├── MacroNAV.csproj
├── MacroNAV.addin
├── PackageContents.xml
├── InstallMacroNAV.bat
└── InstallMacroNAV.ps1
```

---

## Extending

To add a new step type:
1. Add a value to `MacroStepType` in `Models/MacroStep.cs`.
2. Add an icon in `MacroStep.StepTypeIcon()`.
3. Add capture logic in `MacroRecorder.cs`.
4. Add execution logic in `MacroPlayer.cs` (`ExecuteStepAsync` switch).
5. Optionally add a Quick Capture button in `MacroRecorderWindow.xaml`.

---

## Macro JSON Format

Macros are stored/exported as standard JSON (DataContractJsonSerializer). Example step:

```json
{
  "Id": "abc123",
  "StepType": "ClashCreateTest",
  "DisplayName": "Create Clash Test: MECH vs STRUCT",
  "IsEnabled": true,
  "Parameters": {
    "TestName": "MECH vs STRUCT",
    "SelectionA": "Mechanical|Plumbing",
    "SelectionB": "Structure",
    "Tolerance": "0.0010",
    "Type": "HardClash"
  }
}
```

---

## Relationship to AutoNAV

MacroNAV is a standalone companion plugin to [AutoNAV](https://github.com/VDC-Austin-KA/AutoNAV2). AutoNAV steps (`AutoNavSearchSetGen`, `AutoNavClashTestGen`) are recorded as documentation steps — they remind you which AutoNAV workflow to run and with which parameters, but the actual execution is done by opening AutoNAV. Future integration could invoke AutoNAV programmatically via shared DLL reference.
