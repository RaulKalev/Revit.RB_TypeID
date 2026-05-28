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

                var ledger = RbrObjectIdLedgerService.Load(doc);

                using var tx = new Transaction(doc, "Assign RBR Object IDs");
                tx.Start();

                results = RbrObjectIdAssignmentService.AssignIds(
                    doc, selectedIds, SelectedMapping, index);

                // Update ledger for successfully assigned elements.
                var now = DateTime.UtcNow.ToString("o");
                foreach (var r in results.Where(r => r.Status == "Assigned"))
                {
                    var element = doc.GetElement(r.ElementId);
                    if (element == null) continue;

                    LevelCodeService.TryGetLevelCode(element, doc, out string levelCode);
                    var typeEid = element.GetTypeId();
                    string typeName = GetTypeName(element, doc);
                    string prefix = $"{SelectedMapping.DisciplineCode}-{SelectedMapping.ObjectCode}-{levelCode}";

                    var record = new ObjectIdLedgerRecord
                    {
                        ObjectId        = r.NewValue,
                        ElementUniqueId = element.UniqueId ?? string.Empty,
                        ElementId       = r.ElementId.Value,
                        RbrPrCode       = string.Empty, // not available in manual-assign path
                        PbsPartR        = SelectedMapping.DisciplineCode,
                        PbsPartS        = SelectedMapping.ObjectCode,
                        LevelCode       = levelCode ?? string.Empty,
                        Prefix          = prefix,
                        Category        = element.Category?.Name ?? string.Empty,
                        FamilyName      = (element as FamilyInstance)?.Symbol?.FamilyName ?? string.Empty,
                        TypeId          = (typeEid != null && typeEid != ElementId.InvalidElementId)
                                           ? typeEid.Value : 0L,
                        TypeName        = typeName,
                        Fingerprint     = string.Empty,
                        AssignedUtc     = now,
                        UpdatedUtc      = now,
                    };
                    RbrObjectIdLedgerService.UpsertRecord(ledger, record);
                }
                RbrObjectIdLedgerService.Save(doc, ledger);

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

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string GetTypeName(Element element, Document doc)
        {
            var typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId) return string.Empty;
            var type = doc.GetElement(typeId);
            return type?.Name ?? string.Empty;
        }
    }
}
