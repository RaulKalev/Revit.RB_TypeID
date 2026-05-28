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
    /// Reads selected Revit elements, runs the reconcile analysis, and returns
    /// a preview result to the UI.  Does NOT modify the Revit model.
    /// </summary>
    public class PreviewRbrObjectIdReconcileHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────
        public ObjectIdReconcileOptions  Options       { get; set; }
        public PbsPrCodeLookupService    LookupService { get; set; }

        // ── Output callback — dispatched to UI thread ────────────────────────
        public Action<ObjectIdReconcileResult> OnCompleted { get; set; }

        // ── IExternalEventHandler ────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            ObjectIdReconcileResult result = null;

            try
            {
                var uidoc       = app.ActiveUIDocument;
                var doc         = uidoc.Document;
                var selectedIds = uidoc.Selection.GetElementIds().ToList();

                List<ElementId> targetIds;
                bool scannedWholeModel = false;

                if (selectedIds.Count > 0)
                {
                    targetIds = selectedIds;
                }
                else
                {
                    targetIds = CollectCandidateElementIds(doc);
                    scannedWholeModel = true;
                }

                result = RbrObjectIdReconcileService.BuildPreview(
                    doc,
                    targetIds,
                    LookupService,
                    Options ?? new ObjectIdReconcileOptions());

                if (scannedWholeModel)
                {
                    result.Warnings.Add(
                        $"No Revit selection found. Scanned {targetIds.Count} candidate elements in the model.");
                }
            }
            catch (Exception ex)
            {
                result = new ObjectIdReconcileResult();
                result.Warnings.Add("Unhandled error: " + ex.Message);
            }

            OnCompleted?.Invoke(result);
        }

        public string GetName() => "Preview RBR Object ID Reconcile";

        // ── Helpers ──────────────────────────────────────────────────────────

        private static List<ElementId> CollectCandidateElementIds(Document doc)
        {
            var result = new List<ElementId>();

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (element == null) continue;

                bool hasObjectIdParam =
                    RevitParameterResolver.FindObjectIdParameter(element) != null;

                bool hasPrCode =
                    !string.IsNullOrWhiteSpace(RevitParameterResolver.ReadPrCode(element));

                if (hasObjectIdParam || hasPrCode)
                    result.Add(element.Id);
            }

            return result;
        }
    }
}
