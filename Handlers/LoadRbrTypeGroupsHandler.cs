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
    /// Does NOT write anything to the model.
    /// </summary>
    public class LoadRbrTypeGroupsHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────

        public PbsPrCodeLookupService              LookupService      { get; set; }
        public Dictionary<string, PbsTypeNumberRow> TypeNumberLookup  { get; set; }

        /// <summary>When non-empty, only types whose PBS DisciplineCode matches are included.</summary>
        public string SelectedDiscipline { get; set; }

        // ── Output callback — dispatched to UI thread ─────────────────────────

        /// <summary>Invoked with (rows, documentPath) after the handler completes.</summary>
        public Action<List<TypeNumberPreviewRow>, string> OnCompleted { get; set; }

        // ── IExternalEventHandler ─────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            var result = new List<TypeNumberPreviewRow>();

            try
            {
                var uidoc       = app.ActiveUIDocument;
                var doc         = uidoc.Document;
                var selectedIds = uidoc.Selection.GetElementIds().ToList();

                if (selectedIds.Count == 0)
                {
                    OnCompleted?.Invoke(result, doc.PathName ?? doc.Title ?? "default");
                    return;
                }

                // ── Group element instances by ElementType.Id ─────────────────
                var groups = new Dictionary<string, List<Element>>();

                foreach (var id in selectedIds)
                {
                    var element = doc.GetElement(id);
                    if (element == null) continue;

                    var typeId = element.GetTypeId();
                    if (typeId == null || typeId == ElementId.InvalidElementId) continue;

                    string key = typeId.Value.ToString();
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = new List<Element>();
                    list.Add(element);
                }

                // ── Build one preview row per type ────────────────────────────
                foreach (var kv in groups)
                {
                    var first    = kv.Value[0];
                    var typeId   = first.GetTypeId();
                    var elemType = doc.GetElement(typeId) as ElementType;
                    if (elemType == null) continue;

                    var row = BuildRow(elemType, kv.Value, doc);

                    // Apply discipline filter when set.
                    if (!string.IsNullOrWhiteSpace(SelectedDiscipline)
                        && !string.Equals(row.DisciplineCode, SelectedDiscipline,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    result.Add(row);
                }

                // Sort: Category → FamilyName → TypeName
                result = result
                    .OrderBy(r => r.Category)
                    .ThenBy(r => r.FamilyName)
                    .ThenBy(r => r.TypeName)
                    .ToList();
            }
            catch (Exception ex)
            {
                result.Add(new TypeNumberPreviewRow
                {
                    Status  = "Error",
                    Message = ex.Message,
                });
            }

            string docPath = app.ActiveUIDocument?.Document?.PathName
                ?? app.ActiveUIDocument?.Document?.Title
                ?? "default";
            OnCompleted?.Invoke(result, docPath);
        }

        public string GetName() => "Load RBR Type Groups";

        // ── Private ──────────────────────────────────────────────────────────

        private TypeNumberPreviewRow BuildRow(
            ElementType elemType, List<Element> instances, Document doc)
        {
            var row = new TypeNumberPreviewRow
            {
                ElementTypeId = elemType.Id,
                Category      = elemType.Category?.Name ?? string.Empty,
                FamilyName    = (elemType is FamilySymbol fs) ? fs.FamilyName : string.Empty,
                TypeName      = elemType.Name ?? string.Empty,
                InstanceCount = instances.Count,
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

                // Type-number template lookup.
                if (TypeNumberLookup != null
                    && TypeNumberLookup.TryGetValue(normalized, out var tnRow))
                {
                    row.DisciplineCode  = tnRow.DisciplineCode;
                    row.PbsTypeTemplate = tnRow.TypeNumberTemplate ?? string.Empty;
                    row.PbsMatchStatus  = "Matched";

                    // Prefill L1Code: prefix before ZZZZ if template exists, else ObjectCode.
                    if (!string.IsNullOrWhiteSpace(row.PbsTypeTemplate)
                        && row.PbsTypeTemplate.IndexOf("ZZZZ",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int idx   = row.PbsTypeTemplate.IndexOf("ZZZZ",
                            StringComparison.OrdinalIgnoreCase);
                        row.L1Code = row.PbsTypeTemplate.Substring(0, idx);
                    }
                    else
                    {
                        row.L1Code = tnRow.ObjectCode ?? string.Empty;
                    }
                }
                else if (LookupService != null)
                {
                    // No type-number row; fall back to PrCode → R/S lookup for discipline.
                    var lookup = LookupService.FindByPrCode(prCode);
                    if (lookup.Success && lookup.Row != null)
                    {
                        row.DisciplineCode = lookup.Row.ObjectIdPart1 ?? string.Empty;
                        row.PbsMatchStatus = "Matched (no template)";
                        row.L1Code         = lookup.Row.ObjectIdPart2 ?? string.Empty;
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
                // Don't hard-block — user can still fill L1Code manually.
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

            // Leave Status blank until Preview is run; let the user fill L1Code first.
            row.IsSelected = true;
            return row;
        }
    }
}
