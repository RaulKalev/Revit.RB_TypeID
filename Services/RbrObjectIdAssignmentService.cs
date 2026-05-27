using Autodesk.Revit.DB;
using RB_TypeName.Models;
using System.Collections.Generic;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Core assignment logic. Must be called from within an open Transaction.
    /// Never overwrites an existing RBR-Object_ID value.
    /// </summary>
    public static class RbrObjectIdAssignmentService
    {
        public static List<RbrIdAssignmentResult> AssignIds(
            Document doc,
            IEnumerable<ElementId> elementIds,
            PbsMapping mapping,
            RbrObjectIdIndex index)
        {
            var results = new List<RbrIdAssignmentResult>();

            foreach (var elementId in elementIds)
            {
                var element = doc.GetElement(elementId);
                if (element == null) continue;

                var result = new RbrIdAssignmentResult { ElementId = elementId };

                // ── Locate the parameter ────────────────────────────────────
                var idParam = RevitParameterResolver.FindObjectIdParameter(element);
                if (idParam == null)
                {
                    result.Status  = "MissingParameter";
                    result.Message = "Object ID parameter not found on element (tried: "
                        + string.Join(", ", RevitParameterResolver.ObjectIdParameterNames) + ").";
                    results.Add(result);
                    continue;
                }

                // ── Skip if already set (stability rule) ────────────────────
                string existingId = idParam.AsString();
                result.OldValue   = existingId;

                if (!string.IsNullOrWhiteSpace(existingId))
                {
                    result.Status  = "SkippedExisting";
                    result.Message = "ID already exists — kept unchanged.";
                    results.Add(result);
                    continue;
                }

                // ── Read-only guard ─────────────────────────────────────────
                if (idParam.IsReadOnly)
                {
                    result.Status  = "ReadOnly";
                    result.Message = "Parameter is read-only on this element.";
                    results.Add(result);
                    continue;
                }

                // ── Resolve level code ──────────────────────────────────────
                if (!LevelCodeService.TryGetLevelCode(element, doc, out string levelCode))
                {
                    result.Status  = "MissingLevel";
                    result.Message = "Could not determine element level.";
                    results.Add(result);
                    continue;
                }

                // ── Generate and assign the new ID ──────────────────────────
                string prefix    = $"{mapping.DisciplineCode}-{mapping.ObjectCode}-{levelCode}";
                int    nextNum   = index.GetNextNumber(prefix);
                string newId     = $"{prefix}-{nextNum:D4}";

                idParam.Set(newId);
                index.Register(newId);  // Keep index current for the rest of the batch

                result.Status   = "Assigned";
                result.NewValue = newId;
                results.Add(result);
            }

            return results;
        }
    }
}
