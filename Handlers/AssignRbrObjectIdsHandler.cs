using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RB_TypeName.Handlers
{
    public class AssignRbrObjectIdsHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before calling ExternalEvent.Raise() ─
        public PbsMapping SelectedMapping { get; set; }

        // ── Output callback — invoked on Revit API thread; dispatch to UI ───
        public Action<List<RbrIdAssignmentResult>, RbrObjectIdIndex> OnCompleted { get; set; }

        // ── IExternalEventHandler ────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            var results = new List<RbrIdAssignmentResult>();
            RbrObjectIdIndex index = null;

            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc   = uidoc.Document;

                if (SelectedMapping == null)
                {
                    results.Add(new RbrIdAssignmentResult
                    {
                        Status  = "Error",
                        Message = "No PBS mapping selected."
                    });
                    OnCompleted?.Invoke(results, null);
                    return;
                }

                var selectedIds = uidoc.Selection.GetElementIds().ToList();

                if (selectedIds.Count == 0)
                {
                    results.Add(new RbrIdAssignmentResult
                    {
                        Status  = "Error",
                        Message = "No elements selected in Revit. Please select elements first."
                    });
                    OnCompleted?.Invoke(results, null);
                    return;
                }

                // Build index from all existing IDs before touching anything
                index = RbrObjectIdIndex.BuildFromDocument(doc);

                using var tx = new Transaction(doc, "Assign RBR Object IDs");
                tx.Start();

                results = RbrObjectIdAssignmentService.AssignIds(
                    doc, selectedIds, SelectedMapping, index);

                tx.Commit();
            }
            catch (Exception ex)
            {
                results.Add(new RbrIdAssignmentResult
                {
                    Status  = "Error",
                    Message = ex.Message
                });
            }

            OnCompleted?.Invoke(results, index);
        }

        public string GetName() => "Assign RBR Object IDs";
    }
}
