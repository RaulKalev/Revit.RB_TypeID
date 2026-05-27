using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RB_TypeName.Handlers
{
    /// <summary>
    /// Reads the current Revit selection, groups elements by ElementType, performs
    /// PBS lookup to prefill L1 codes and type number templates, and builds a
    /// TypeNumberPreviewRow list for the Type Numbers tab.
    /// Also loads saved L1Code mappings from Extensible Storage and applies them.
    /// Saves current UI settings to Extensible Storage.
    /// </summary>
    public class LoadRbrTypeGroupsHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────

        public PbsPrCodeLookupService               LookupService      { get; set; }
        public Dictionary<string, PbsTypeNumberRow>  TypeNumberLookup  { get; set; }

        /// <summary>When non-empty, only types whose PBS DisciplineCode matches are included.</summary>
        public string SelectedDiscipline { get; set; }

        /// <summary>
        /// Parameter name that identifies the "type" within a type group.
        /// Special values: "Revit Type Name" (default), "FamilyName + TypeName".
        /// Any other value is treated as a parameter name to look up on the element.
        /// </summary>
        public string TypeSourceParameterName { get; set; } = "Revit Type Name";

        /// <summary>Folder containing old per-document NDJSON mapping files (for migration).</summary>
        public string SettingsFolder { get; set; }

        // ── Output — read by the UI callback ─────────────────────────────────

        /// <summary>
        /// Parameter names discovered from the loaded elements.
        /// Always starts with "Revit Type Name" and "FamilyName + TypeName".
        /// </summary>
        public List<string> DiscoveredParameterNames { get; private set; } = new List<string>();

        /// <summary>Settings restored from Extensible Storage (null if none saved).</summary>
        public TypeNumberSettings RestoredSettings { get; private set; }

        /// <summary>Total element instances scanned from the document.</summary>
        public int ScannedInstanceCount { get; private set; }

        /// <summary>Number of unique element types found across the selection.</summary>
        public int TypeGroupCount { get; private set; }

        /// <summary>Number of rows dropped by the active discipline filter.</summary>
        public int FilteredOutCount { get; private set; }

        // ── Output callback — dispatched to UI thread ─────────────────────────

        /// <summary>Invoked with (rows, documentPath, discoveredParamNames) after completion.</summary>
        public Action<List<TypeNumberPreviewRow>, string, List<string>> OnCompleted { get; set; }

        // ── IExternalEventHandler ─────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            var result = new List<TypeNumberPreviewRow>();
            ScannedInstanceCount = 0;
            TypeGroupCount       = 0;
            FilteredOutCount     = 0;

            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc   = uidoc.Document;

                // Load settings from Extensible Storage.
                // Settings are saved only when the user explicitly triggers Save/Import/Apply.
                RestoredSettings = TypeNumberSettingsStorageService.Load(doc);

                string docPath = doc.PathName ?? doc.Title ?? "default";

                // ── Group ALL element instances in the document by ElementType.Id ──
                var groups = new Dictionary<string, List<Element>>();

                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                foreach (var element in collector)
                {
                    // Skip Revit links, DWG/DXF/IFC imports & links, and views.
                    if (element is RevitLinkInstance) continue;
                    if (element is ImportInstance)    continue;   // DWG, DXF, linked IFC
                    if (element is View)              continue;

                    // Skip view-specific elements (detail components, detail lines,
                    // filled regions, repeating details, etc.).
                    if (element.ViewSpecificId != ElementId.InvalidElementId) continue;

                    // Skip annotation/2D elements and internal Revit categories.
                    // Only CategoryType.Model represents placed 3D model elements.
                    var cat = element.Category;
                    if (cat == null || cat.CategoryType != CategoryType.Model) continue;

                    ScannedInstanceCount++;

                    var typeId = element.GetTypeId();
                    if (typeId == null || typeId == ElementId.InvalidElementId) continue;

                    string key = typeId.Value.ToString();
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = new List<Element>();
                    list.Add(element);
                }

                TypeGroupCount = groups.Count;

                // ── Discover parameter names from a sample of elements ─────────
                DiscoveredParameterNames = DiscoverParameterNames(groups, doc);

                // ── Build one preview row per type ────────────────────────────
                string typeSourceParam = TypeSourceParameterName ?? "Revit Type Name";

                foreach (var kv in groups)
                {
                    var first    = kv.Value[0];
                    var typeId   = first.GetTypeId();
                    var elemType = doc.GetElement(typeId) as ElementType;
                    if (elemType == null) continue;

                    var row = BuildRow(elemType, kv.Value, doc, typeSourceParam);

                    // Apply discipline filter when set.
                    if (!string.IsNullOrWhiteSpace(SelectedDiscipline)
                        && !string.Equals(row.DisciplineCode, SelectedDiscipline,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        FilteredOutCount++;
                        continue;
                    }

                    result.Add(row);
                }

                // Sort: Category → FamilyName → TypeName
                result = result
                    .OrderBy(r => r.Category)
                    .ThenBy(r => r.FamilyName)
                    .ThenBy(r => r.TypeName)
                    .ToList();

                // ── Load saved mappings from Extensible Storage ───────────────
                var savedMappings = TypeNumberMappingExtensibleStorageService.Load(doc);

                // If no ExtStorage mappings, try migration from old local NDJSON.
                if (savedMappings.Count == 0 && !string.IsNullOrWhiteSpace(SettingsFolder))
                {
                    string oldNdjson = TypeNumberMappingStorageService
                        .GetStorageFilePath(SettingsFolder, docPath);
                    var migrated = TypeNumberMappingExtensibleStorageService
                        .MigrateFromLocalNdjson(oldNdjson);

                    if (migrated.Count > 0)
                    {
                        // Apply migrated records directly to rows using fuzzy match (RbrPrCode + TypeName).
                        ApplyMigratedMappings(result, migrated);

                        // Persist migrated records to ExtStorage.
                        TrySaveMigratedMappings(doc, migrated);
                    }
                }
                else
                {
                    TypeNumberMappingExtensibleStorageService.ApplyToRows(result, savedMappings);
                }
            }
            catch (Exception ex)
            {
                result.Add(new TypeNumberPreviewRow
                {
                    Status  = "Error",
                    Message = ex.Message,
                });
            }

            string completedDocPath = app.ActiveUIDocument?.Document?.PathName
                ?? app.ActiveUIDocument?.Document?.Title
                ?? "default";
            OnCompleted?.Invoke(result, completedDocPath, DiscoveredParameterNames);
        }

        public string GetName() => "Load RBR Type Groups";

        // ── Private — migration ───────────────────────────────────────────────

        private void TrySaveMigratedMappings(Document doc,
            IEnumerable<TypeNumberMappingRecord> records)
        {
            try
            {
                using var t = new Transaction(doc, "Migrate RBR Type Number Mappings");
                t.Start();
                TypeNumberMappingExtensibleStorageService.Save(doc, records);
                t.Commit();
            }
            catch { /* non-critical */ }
        }

        // ── Private — parameter discovery ────────────────────────────────────

        private List<string> DiscoverParameterNames(
            Dictionary<string, List<Element>> groups, Document doc)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int sampled = 0;

            foreach (var kv in groups)
            {
                var inst = kv.Value.First();

                // Instance parameters.
                foreach (Parameter p in inst.Parameters)
                {
                    if (p.StorageType == StorageType.String)
                        names.Add(p.Definition.Name);
                }

                // Type parameters.
                var typeId   = inst.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var elemType = doc.GetElement(typeId) as ElementType;
                    if (elemType != null)
                    {
                        foreach (Parameter p in elemType.Parameters)
                        {
                            if (p.StorageType == StorageType.String)
                                names.Add(p.Definition.Name);
                        }
                    }
                }

                if (++sampled >= 10) break;
            }

            var result = DefaultParamNames();
            result.AddRange(names
                .Where(n =>
                    !string.Equals(n, "Revit Type Name",      StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(n, "FamilyName + TypeName", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n));
            return result;
        }

        private static List<string> DefaultParamNames()
            => new List<string> { "Revit Type Name", "FamilyName + TypeName" };

        // ── Private — type source value ────────────────────────────────────────

        private static string ReadTypeSourceValue(
            string paramName, ElementType elemType, List<Element> instances)
        {
            if (string.IsNullOrWhiteSpace(paramName)
                || string.Equals(paramName, "Revit Type Name",
                    StringComparison.OrdinalIgnoreCase))
                return elemType.Name ?? string.Empty;

            if (string.Equals(paramName, "FamilyName + TypeName",
                    StringComparison.OrdinalIgnoreCase))
            {
                string fam = (elemType is FamilySymbol fs)
                    ? fs.FamilyName
                    : elemType.Category?.Name ?? string.Empty;
                return fam + " : " + (elemType.Name ?? string.Empty);
            }

            // Type-level parameter.
            var p = elemType.LookupParameter(paramName);
            if (p != null)
            {
                string v = p.AsString() ?? p.AsValueString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }

            // Fall back to instance-level parameter.
            foreach (var inst in instances)
            {
                p = inst.LookupParameter(paramName);
                if (p != null)
                {
                    string v = p.AsString() ?? p.AsValueString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }

            return string.Empty;
        }

        // ── Private — migration fuzzy-match ───────────────────────────────────

        private static void ApplyMigratedMappings(
            List<TypeNumberPreviewRow> rows,
            List<TypeNumberMappingRecord> migrated)
        {
            // Index old records by RbrPrCode + TypeSourceValue for fuzzy lookup.
            var index = new Dictionary<string, TypeNumberMappingRecord>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var r in migrated)
            {
                string k = Norm(r.RbrPrCode) + "|" + Norm(r.TypeSourceValue);
                if (!index.ContainsKey(k)) index[k] = r;
            }

            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.L1Code)) continue;
                string key = Norm(row.RbrPrCode) + "|" + Norm(row.TypeSourceValue);
                if (index.TryGetValue(key, out var rec) && !string.IsNullOrWhiteSpace(rec.L1Code))
                    row.ApplyL1CodeFromMapping(rec.L1Code, "Migrated", hasSavedMapping: true);
            }
        }

        private static string Norm(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();

        // ── Private — BuildRow ────────────────────────────────────────────────

        private TypeNumberPreviewRow BuildRow(
            ElementType elemType, List<Element> instances, Document doc,
            string typeSourceParam)
        {
            var row = new TypeNumberPreviewRow
            {
                ElementTypeId         = elemType.Id,
                Category              = elemType.Category?.Name ?? string.Empty,
                FamilyName            = (elemType is FamilySymbol fs) ? fs.FamilyName : string.Empty,
                TypeName              = elemType.Name ?? string.Empty,
                InstanceCount         = instances.Count,
                TypeSourceParameterName = typeSourceParam,
                TypeSourceValue       = ReadTypeSourceValue(typeSourceParam, elemType, instances),
            };

            // ── Read PrCode from representative instance ──────────────────────
            string prCode = null;
            foreach (var inst in instances)
            {
                prCode = RevitParameterResolver.ReadPrCode(inst);
                if (!string.IsNullOrWhiteSpace(prCode)) break;
            }
            row.RbrPrCode = prCode ?? string.Empty;

            // ── PBS lookups ───────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(prCode))
            {
                string normalized = prCode.Trim()
                    .Replace("\u00A0", " ")
                    .ToUpperInvariant();

                if (TypeNumberLookup != null
                    && TypeNumberLookup.TryGetValue(normalized, out var tnRow))
                {
                    row.DisciplineCode  = tnRow.DisciplineCode;
                    row.PbsTypeTemplate = tnRow.TypeNumberTemplate ?? string.Empty;
                    row.PbsMatchStatus  = "Matched";

                    // PBS prefill: use ApplyL1CodeFromPbs so it sets MappingStatus = "PBS suggestion"
                    // without overwriting any subsequently-loaded saved mapping.
                    string pbsL1;
                    if (!string.IsNullOrWhiteSpace(row.PbsTypeTemplate)
                        && row.PbsTypeTemplate.IndexOf("ZZZZ",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int idx = row.PbsTypeTemplate.IndexOf("ZZZZ",
                            StringComparison.OrdinalIgnoreCase);
                        pbsL1 = row.PbsTypeTemplate.Substring(0, idx);
                    }
                    else
                    {
                        pbsL1 = tnRow.ObjectCode ?? string.Empty;
                    }
                    row.ApplyL1CodeFromPbs(pbsL1);
                }
                else if (LookupService != null)
                {
                    var lookup = LookupService.FindByPrCode(prCode);
                    if (lookup.Success && lookup.Row != null)
                    {
                        row.DisciplineCode = lookup.Row.ObjectIdPart1 ?? string.Empty;
                        row.PbsMatchStatus = "Matched (no template)";
                        row.ApplyL1CodeFromPbs(lookup.Row.ObjectIdPart2 ?? string.Empty);
                    }
                    else
                    {
                        row.PbsMatchStatus = lookup.Status;
                    }
                }
                else
                {
                    row.PbsMatchStatus = "No PBS lookup available";
                }
            }
            else
            {
                row.PbsMatchStatus = "Missing RBR_Pr_Code";
            }

            // ── Read existing type number parameter ───────────────────────────
            var typeParam = RevitParameterResolver.FindTypeNumberParameter(elemType);
            if (typeParam == null)
            {
                row.Status     = "Missing RBR-Type_number parameter";
                row.Message    = "The RBR-Type_number shared parameter is not bound to this element type.";
                row.IsSelected = false;
                return row;
            }

            if (typeParam.IsReadOnly)
            {
                row.Status     = "RBR-Type_number parameter read-only";
                row.Message    = "The parameter exists but cannot be written.";
                row.IsSelected = false;
                return row;
            }

            string existing = typeParam.AsString();
            row.ExistingTypeNumber = existing ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(existing))
            {
                row.Status     = "Already has Type Number";
                row.IsSelected = false;
                return row;
            }

            row.IsSelected = true;
            return row;
        }
    }
}
