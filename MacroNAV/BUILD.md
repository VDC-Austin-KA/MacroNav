# Building MacroNAV

MacroNAV ships **two separate DLLs** — one compiled against the NW2025 API
and one against NW2027 — because those versions have incompatible Clash
Detective APIs. The compile-time `#if NW2025` / `#if NW2027` guards in
`ClashCompat.cs` paper over the differences.

## Prerequisites

- Visual Studio 2019+ **or** MSBuild 17+ (installed with VS Build Tools)
- .NET Framework 4.8 SDK
- Autodesk Navisworks Manage **2025** and/or **2027** installed locally
  (the build needs their reference DLLs)

## Build commands

```bat
REM From the repo root — build both targets in one go:
msbuild MacroNAV\MacroNAV.csproj /p:Platform=x64 /p:Configuration=Release-NW2025
msbuild MacroNAV\MacroNAV.csproj /p:Platform=x64 /p:Configuration=Release-NW2027
```

Output locations:

| Config | Output folder | DefineConstant |
|---|---|---|
| `Release-NW2025` | `MacroNAV\bin\Release-NW2025\` | `NW2025` |
| `Release-NW2027` | `MacroNAV\bin\Release-NW2027\` | `NW2027` |

## Install

After building, run the installer from the **project** folder (not a bin folder):

```bat
cd MacroNAV
InstallMacroNAV.bat
```

or (as Administrator):

```powershell
cd MacroNAV
.\InstallMacroNAV.ps1
```

The installer copies the correct DLL into each detected Navisworks installation.
Macro data (`%AppData%\MacroNAV\macros.json`) is shared between both.

## Key API differences: NW2025 vs NW2027

All version-specific code is isolated in `ClashCompat.cs`:

| Area | NW2025 | NW2027 |
|---|---|---|
| Test collection | `dct.Tests` | `dct.Value.TestsRoot.Children` |
| Add new test | `dct.Tests.AddNewClashTest()` | `dct.Value.TestsRoot.AddNewClashTest()` |
| Copy test to root | `dct.TestsAddCopy(test)` | `dct.TestsAddCopy(root, test)` |
| Replace test | `dct.TestsReplaceWithCopy(i, t)` | `dct.TestsReplaceWithCopy(root, i, t)` |
| `ClashResult.AssignedTo` | `string` | `Assignee` (use `.DisplayName`) |
| `ClashResult.ApprovedBy` | `string` | `Assignee` (use `.DisplayName`) |

To add support for a new version: add a `Release-NWxxxx` configuration in
`MacroNAV.csproj`, define the `NWxxxx` constant, and update the `#if` blocks
in `ClashCompat.cs` as needed.
