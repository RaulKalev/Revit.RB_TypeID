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
    /// Reads selected Revit elements and builds a preview list of proposed RBR-Object_IDs
    /// using the RBR_Pr_Code → PBS lookup workflow.
    /// Does NOT modify the Revit model — no Transaction is opened.
    /// </summary>
    public class PreviewRbrObjectIdsHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────
        public PbsPrCodeLookupService LookupService { get; set; }

        // ── Output callback — dispatch to UI thread ──────────────────────────
        public Action<List<ObjectIdPreviewRow>, RbrObjectIdIndex> OnCompleted { get; set; }

        // ── IExternalEventHandler ────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            var previewRows = new List<ObjectIdPreviewRow>();
            RbrObjectIdIndex index = null;

            try
            {
                var uidoc       = app.ActiveUIDocument;
                var doc         = uidoc.Document;
                var selectedIds = uidoc.Selection.GetElementIds().ToList();

                if (selectedIds.Count == 0)
                {
                    OnCompleted?.Invoke(previewRows, null);
                    return;
                }

                // Build index from all existing IDs in the document.
                index = RbrObjectIdIndex.BuildFromDocument(doc);

                foreach (var elementId in selectedIds.OrderBy(id => id.Value))
                {
                    var element = doc.GetElement(elementId);
                    if (element == null) continue;

                    var row = BuildPreviewRow(element, doc, index);
                    previewRows.Add(row);
                }
            }
            catch (Exception ex)
            {
                previewRows.Add(new ObjectIdPreviewRow
                {
                    Status  = "Error",
                    Message = ex.Message,
                });
            }

            OnCompleted?.Invoke(previewRows, index);
        }

        public string GetName() => "Preview RBR Object IDs";

        // ── Private ──────────────────────────────────────────────────────────

        private ObjectIdPreviewRow BuildPreviewRow(
            Element element, Document doc, RbrObjectIdIndex index)
        {
            var row = new ObjectIdPreviewRow
            {
                ElementId  = element.Id,
                Category   = element.Category?.Name ?? string.Empty,
                FamilyName = (element as FamilyInstance)?.Symbol?.FamilyName ?? string.Empty,
                TypeName   = GetTypeName(element, doc),
            };

            // ── Check Object ID parameter ────────────────────────────────────
            var objectIdParam = RevitParameterResolver.FindObjectIdParameter(element);
            if (objectIdParam == null)
            {
                row.Status     = "Missing RBR-Object_ID parameter";
                row.IsSelected = false;
                return row;
            }

            if (objectIdParam.IsReadOnly)
            {
                row.Status     = "RBR-Object_ID parameter read-only";
                row.IsSelected = false;
                return row;
            }

            string existingId = objectIdParam.AsString();
            row.ExistingObjectId = existingId ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(existingId))
            {
                row.Status     = "Already has Object ID";
                row.IsSelected = false;
                return row;
            }

            // ── Read RBR_Pr_Code ─────────────────────────────────────────────
            string prCode = RevitParameterResolver.ReadPrCode(element);
            row.RbrPrCode = prCode ?? string.Empty;

            if (string.IsNullOrWhiteSpace(prCode))
            {
                row.Status     = "Missing RBR_Pr_Code";
                row.IsSelected = false;
                return row;
            }

            if (LookupService == null)
            {
                row.Status     = "No PBS lookup available";
                row.IsSelected = false;
                return row;
            }

            // ── PBS lookup ───────────────────────────────────────────────────
            var lookupResult = LookupService.FindByPrCode(prCode);
            if (!lookupResult.Success)
            {
                row.Status     = lookupResult.Status;
                row.Message    = lookupResult.Message ?? string.Empty;
                row.IsSelected = false;
                return row;
            }

            row.PbsPartR = lookupResult.Row.ObjectIdPart1;
            row.PbsPartS = lookupResult.Row.ObjectIdPart2;
            row.Message  = lookupResult.Warning ?? string.Empty;

            if (string.IsNullOrWhiteSpace(row.PbsPartR) || string.IsNullOrWhiteSpace(row.PbsPartS))
            {
                row.Status     = "Missing PBS R/S code";
                row.IsSelected = false;
                return row;
            }

            // ── Resolve level ────────────────────────────────────────────────
            if (!LevelCodeService.TryGetLevelAndCode(element, doc,
                    out string levelName, out string levelCode))
            {
                row.Status     = "Missing level";
                row.IsSelected = false;
                return row;
            }

            row.LevelName = levelName;
            row.LevelCode = levelCode;

            // ── Generate proposed ID ─────────────────────────────────────────
            string prefix   = $"{row.PbsPartR}-{row.PbsPartS}-{levelCode}";
            int    nextNum  = index.GetNextNumber(prefix);
            string proposed = $"{prefix}-{nextNum:D4}";

            // Reserve in the index so the next element in the same batch increments correctly.
            index.Register(proposed);

            row.ProposedObjectId = proposed;
            row.Status           = "Ready";
            return row;
        }

        private static string GetTypeName(Element element, Document doc)
        {
            var typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId) return string.Empty;
            return (doc.GetElement(typeId) as ElementType)?.Name ?? string.Empty;
        }
    }
}
