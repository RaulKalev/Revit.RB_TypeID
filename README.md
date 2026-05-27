# RB TypeName — RBR Object ID

A Revit plugin that auto-assigns **`RBR-Object_ID`** shared parameters to elements,
built as part of the **RK Tools** ecosystem.

## What it does

- Reads a PBS (Project Breakdown Structure) Excel file to get discipline/object code mappings
- Assigns unique IDs in the format **`DISC-OBJ-L01-0001`** to all model elements matching the selected mapping
- Skips elements that already have an ID (non-destructive)
- Detects and reports duplicate / malformed IDs already in the model
- Configurable PBS Excel source: choose sheet name, header row, and which columns carry each field

## ID format

```
ME-DUC-L01-0001
│   │   │   └── Running number (4 digits, per-prefix counter)
│   │   └────── Level code (e.g. B01, L01, R01)
│   └────────── Object code (from PBS column S)
└────────────── Discipline code (from PBS column R)
```

## Architecture

| File | Purpose |
|---|---|
| `App.cs` | `IExternalApplication` entry point (`[AppLoader]`), ribbon button, ExternalEvent registration |
| `Commands/TypeNameCommand.cs` | `IExternalCommand` — opens the UI via Idling pattern |
| `Models/PbsMapping.cs` | PBS row data (discipline code, object code, description) |
| `Models/PbsExcelSourceSettings.cs` | Configurable PBS source settings, JSON persistence |
| `Models/PbsLoadResult.cs` | Success/failure wrapper for PBS load operations |
| `Models/ParsedRbrObjectId.cs` | Parsed representation of an existing `RBR-Object_ID` |
| `Models/RbrIdAssignmentResult.cs` | Per-element assignment result |
| `Services/PbsMappingService.cs` | Reads PBS `.xlsx` via ZIP+XML (no NuGet Excel deps) |
| `Services/LevelCodeService.cs` | Resolves a Revit element → level code string |
| `Services/RbrObjectIdIndex.cs` | In-memory index of existing IDs; duplicate/malformed detection |
| `Services/RbrObjectIdAssignmentService.cs` | Core assignment loop |
| `Handlers/AssignRbrObjectIdsHandler.cs` | `IExternalEventHandler` — runs inside a Revit transaction |
| `UI/RbrObjectIdWindow.xaml(.cs)` | WPF window — PBS file picker, mapping selector, results grid |

## Build targets

| Target | Revit version |
|---|---|
| `net48` | Revit 2024 |
| `net8.0-windows` | Revit 2026 |

## Requirements

- Autodesk Revit 2024 or 2026
- [ricaun.AppLoader](https://github.com/ricaun-io/ricaun.AppLoader) — drop the built DLL into AppLoader's watched folder; no `.addin` file needed
- The model must have a shared parameter named **`RBR-Object_ID`** bound to the relevant categories

## Dependencies (NuGet)

| Package | Purpose |
|---|---|
| `Autodesk.Revit.SDK` | Revit API references |
| `ricaun.Revit.UI` | Ribbon helpers + `[AppLoader]` attribute |
| `Costura.Fody` + `Fody` | Embeds dependencies into a single self-contained DLL |

## Settings persistence

Settings are stored in:
```
%LocalAppData%\RK Tools\RB_TypeName\settings.json
```

Default PBS column mapping:

| Field | Column |
|---|---|
| Discipline code | R |
| Object code | S |
| Secondary object code | T |
| Description | K |
| Header rows to skip | 2 |
