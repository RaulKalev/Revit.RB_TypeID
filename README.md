# RB TypeName — RBR Object ID & Type Numbers

A Revit plugin that auto-assigns **`RBR-Object_ID`** and **`RBR-Type_number`** shared parameters to elements,
built as part of the **RK Tools** ecosystem.

## What it does

- Reads a PBS (Project Breakdown Structure) Excel file to get discipline/object code mappings
- **Object IDs tab** — Assigns unique IDs in the format **`DISC-OBJ-L01-0001`** to selected model element *instances*
- **Type Numbers tab** — Assigns unique running type numbers in the format **`CAM-010001`** to Revit *ElementTypes*
- Skips elements/types that already have a value (non-destructive)
- Detects and reports duplicate / malformed IDs already in the model
- PBS Excel source path is selectable in the UI. Sheet name and column mappings are stored in settings and currently use project defaults unless edited in the settings JSON or exposed later in the UI.

## Object IDs workflow (Tab 1)

1. Select a PBS Excel file.
2. Select elements in Revit.
3. Click **Preview Object IDs**.
4. Review proposed values in the grid.
5. Select rows to apply.
6. Click **Apply Object IDs**.

By default, Object IDs are generated from the element's `RBR_Pr_Code` parameter.
The plugin looks up that value in the selected PBS Excel file and uses PBS columns R and S to build the Object ID prefix.

If manual fallback is enabled, the selected PBS mapping is used when `RBR_Pr_Code` is missing or cannot be matched to a PBS row.
Fallback rows are marked in the preview **Message** column.

### Object ID ledger (Extensible Storage)

Every time an Object ID is assigned — whether via **Preview → Apply**, **Assign (manual)**, or **Apply Selected Fixes** — the plugin records a ledger entry in the Revit model using **Extensible Storage** (schema `RKTools_RbrObjectIdLedger`).

The ledger stores:
- The element's stable `UniqueId` and last-known `ElementId`
- The PBS prefix, level code, type name, family name, and `RBR_Pr_Code` at the time of assignment
- A fingerprint string combining these fields — used to detect if the element has changed
- Assignment and last-update timestamps
- A history of previous Object IDs (if an ID was reassigned)

Retired/replaced IDs are also kept in a separate list so the ledger can reliably prevent their reuse even after the originating elements are deleted.

### Update / Reconcile IDs

The **Update / Reconcile IDs** section provides a non-destructive way to review the state of Object IDs across a selection (or the whole model if nothing is selected) and apply targeted fixes.

**Options:**
- **Include missing IDs** — show elements that have no Object ID at all
- **Repair copied / duplicate IDs** — detect elements sharing the same ID (e.g. after Revit copy/paste), propose new IDs for the non-original copies
- **Flag changed type / level** — use the ledger fingerprint to detect elements whose type, level, or `RBR_Pr_Code` has changed since the ID was assigned
- **Reassign changed type / level IDs** — if enabled, also propose new IDs for elements that have changed (off by default — use intentionally)
- **Show valid rows** — include rows with no issues (useful for auditing)

**Workflow:**
1. Optionally pre-select elements (otherwise the whole model is scanned).
2. Set options.
3. Click **Update IDs** — builds a preview in the Reconcile grid.
4. Review the grid, deselect rows you do not want to change.
5. Click **Apply Selected Fixes** — writes proposed IDs and updates the ledger.

**Duplicate resolution logic:**
- If exactly one element is the ledger-recorded owner of the duplicated ID, that element keeps its ID (status: `DuplicateKeeper`) and the copy receives a new one (status: `DuplicateNeedsNew`).
- If no ledger entry exists, the element with the lowest `ElementId` value keeps the ID.
- If multiple ledger entries claim the same ID (ambiguous), all copies are flagged as `Ambiguous` and the user is prompted to resolve manually.

## Type Numbers workflow (Tab 2)

1. Select a PBS Excel file (same file, shared).
2. Select element *instances* in Revit (the plugin reads their ElementType automatically).
3. Optionally filter by **Discipline** in the toolbar dropdown.
4. Click **Load Types from Selection** — groups instances by ElementType and prefills **L1 Code** from any previously saved mapping.
5. Review the grid; edit **L1 Code** cells where needed.
6. Click **Preview Type Numbers** — assigns running numbers per prefix, consulting both the model and the ledger file.
7. Click **Apply Type Numbers** to write values.

The L1 Code column is pre-filled from the PBS type-number template (text before `ZZZZ`) when a match exists.
Previously saved mappings (see below) are loaded automatically and override the PBS suggestion.
Proposed numbers are always append-only: the plugin tracks the highest number already used (in the model) **and** the last number it issued (in the ledger file) to prevent reuse after types are deleted.

A per-document ledger file is saved to:
```
%LocalAppData%\RK Tools\RB_TypeName\TypeNumberLedger_<documentKey>.json
```

## Type Number Mapping — Excel Export / Import

The toolbar in the Type Numbers tab includes two additional buttons for bulk L1 Code management via Excel.

### Export Mapping…

1. Click **Export Mapping…** after loading types.
2. Choose export scope: **All rows**, **Only unmapped rows**, or **Only mapped rows**.
3. Save the generated `.xlsx` file (`RBR_TypeNumber_Mapping_<timestamp>.xlsx`).
4. Open the file in Excel. Columns A–D and F–M are reference data. Column **E (`RBR_ObjectID_Character_Level1`)** and column **N (`Notes`)** are intended for user editing.

### Import Mapping…

1. Click **Import Mapping…** and select a previously exported (or manually created) `.xlsx` file.
2. The plugin validates headers, checks L1 code format (`^[A-Z0-9_\-]+$`), and reports any invalid or conflicting rows.
3. Choose an import mode:
   - **Override existing mappings** — replaces stored L1 codes with the imported values and immediately updates the grid.
   - **Only add new mappings** — keeps existing stored L1 codes; only fills in entries with no stored mapping.
4. All valid imported records are saved directly to Revit Extensible Storage inside the model, independent of which rows are currently visible in the grid.

Mappings are stored inside the Revit model using **Extensible Storage** (schema `RKTools_RbrTypeNumberMappings`). They travel with the model file and require no external file system access.

The match key used for import/export is:
```
DisciplineCode | RBR_Pr_Code | TypeSourceParameterName | TypeSourceValue
```
(normalised, case-insensitive) — stable across Revit sessions as long as the type source parameter selection is consistent.

> **Legacy migration** — if no Extensible Storage mappings exist on first load, the plugin reads the old per-document NDJSON file (`%LocalAppData%\RK Tools\RB_TypeName\TypeNumberMappings_<documentKey>.json`) once and migrates its records into Extensible Storage. The local file is **not** deleted automatically.

## Object ID format

```
ME-DUC-L01-0001
│   │   │   └── Running number (4 digits, per-prefix counter)
│   │   └────── Level code (e.g. B01, L01, R01) — extracted from level name before the first _
│   └────────── Object code (from PBS column S)
└────────────── Discipline code (from PBS column R)
```

Example level name extraction: `B01_Kelder` → `B01`, `L01_1 korrus` → `L01`.

## Type Number format

The PBS template (for example `CAM-01ZZZZ`) is used only to prefill the L1 Code suggestion. The **final generated prefix always comes from the user-entered (or saved) `L1 Code`** — the PBS template is never used as the prefix on its own.

```
CAM-020001
│      └── Running number (4 digits, substitutes ZZZZ placeholder)
└───────── Prefix (user/saved L1 Code, e.g. CAM-02)
```

Example:

- PBS template: `CAM-01ZZZZ`
- User L1 Code: `CAM-02`
- Generated value: `CAM-020001`

When no PBS template is present, the format is `<L1Code>-<0001>`.

## Current limitations

- `RBR-Object_IDparent` is not assigned by this version.
- Existing `RBR-Object_ID` or `RBR-Type_number` values are never overwritten automatically.

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
| `Models/ObjectIdPreviewRow.cs` | DataGrid row for the Object IDs tab |
| `Models/TypeNumberPreviewRow.cs` | DataGrid row for the Type Numbers tab; exposes computed `MatchKey` used by mapping storage |
| `Models/PbsTypeNumberRow.cs` | PBS row carrying type-number template data |
| `Models/TypeNumberMappingRecord.cs` | Persisted mapping record — key fields: DisciplineCode, RBR_Pr_Code, TypeSourceParameterName, TypeSourceValue; value: L1Code + Notes |
| `Models/TypeNumberMappingUpsertResult.cs` | Counts returned by the Extensible Storage upsert operation (Added/Updated/SkippedExisting/Invalid) |
| `Models/TypeNumberMappingImportResult.cs` | Import summary: row counts and per-row issue messages |
| `Services/PbsMappingService.cs` | Reads PBS `.xlsx` via ZIP+XML (no NuGet Excel deps) |
| `Services/LevelCodeService.cs` | Resolves a Revit element → level code string |
| `Services/RbrObjectIdIndex.cs` | In-memory index of existing Object IDs; duplicate/malformed detection |
| `Services/RbrObjectIdAssignmentService.cs` | Core Object ID assignment loop |
| `Services/RbrTypeNumberIndex.cs` | In-memory index of existing Type Numbers; tracks max per prefix |
| `Services/RbrTypeNumberLedgerService.cs` | JSON ledger of last-issued numbers per prefix, per document |
| `Services/TypeNumberMappingStorageService.cs` | **Legacy** per-document NDJSON mapping reader — used only as a one-time migration source; not the primary storage |
| `Services/TypeNumberMappingExtensibleStorageService.cs` | Stores type-number mappings in Revit Extensible Storage (JSON blob, schema `RKTools_RbrTypeNumberMappings`). `Load()` is read-only; `Save()` / `Upsert()` require an active Transaction |
| `Services/TypeNumberSettingsStorageService.cs` | Stores Type Numbers tab settings (selected TypeSource param, selected Discipline) in Revit Extensible Storage (schema `RKTools_RbrTypeNumberSettings`) |
| `Services/TypeNumberMappingExcelService.cs` | xlsx export/import for bulk L1 Code management; uses ZIP+XML (no new NuGet deps); includes `TypeNumberExcelExportMode` / `TypeNumberExcelImportMode` enums |
| `Services/RevitParameterResolver.cs` | Parameter lookup helpers for `RBR_Pr_Code`, `RBR-Object_ID`, `RBR-Type_number` |
| `Handlers/AssignRbrObjectIdsHandler.cs` | `IExternalEventHandler` — runs inside a Revit transaction |
| `Handlers/PreviewRbrObjectIdsHandler.cs` | `IExternalEventHandler` — builds Object ID preview without modifying the model |
| `Handlers/ApplyRbrObjectIdsHandler.cs` | `IExternalEventHandler` — applies previewed Object IDs in a transaction |
| `Handlers/LoadRbrTypeGroupsHandler.cs` | `IExternalEventHandler` — groups selected elements by ElementType, performs PBS lookup, discovers type-source parameters, loads saved mappings from Extensible Storage, migrates old NDJSON on first run |
| `Handlers/SaveTypeNumberMappingsHandler.cs` | `IExternalEventHandler` — upserts current grid L1Codes into Extensible Storage (merge, never replaces unrelated mappings); saves current TypeSource/Discipline settings |
| `Handlers/ImportTypeNumberMappingsHandler.cs` | `IExternalEventHandler` — saves all parsed Excel records to Extensible Storage via `Upsert()`, independent of which rows are visible in the grid |
| `Handlers/PreviewRbrTypeNumbersHandler.cs` | `IExternalEventHandler` — generates proposed Type Numbers without modifying the model |
| `Handlers/ApplyRbrTypeNumbersHandler.cs` | `IExternalEventHandler` — writes Type Numbers to ElementTypes, saves ledger |
| `UI/RbrObjectIdWindow.xaml(.cs)` | WPF window — two-tab layout (Object IDs / Type Numbers), shared PBS file picker, Export/Import Mapping buttons with mode-choice dialogs |
| `Services/RbrObjectIdAssignmentService.cs` | Core assignment loop |
| `Handlers/AssignRbrObjectIdsHandler.cs` | `IExternalEventHandler` — runs inside a Revit transaction |
| `Handlers/PreviewRbrObjectIdsHandler.cs` | `IExternalEventHandler` — builds preview without modifying the model |
| `Handlers/ApplyRbrObjectIdsHandler.cs` | `IExternalEventHandler` — applies previewed IDs in a transaction |
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
