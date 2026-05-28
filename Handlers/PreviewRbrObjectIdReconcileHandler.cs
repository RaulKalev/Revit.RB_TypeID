using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
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

                if (selectedIds.Count == 0)
                {
                    result = new ObjectIdReconcileResult();
                    result.Warnings.Add("No elements selected in Revit.");
                }
                else
                {
                    result = RbrObjectIdReconcileService.BuildPreview(
                        doc,
                        selectedIds,
                        LookupService,
                        Options ?? new ObjectIdReconcileOptions());
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
    }
}
