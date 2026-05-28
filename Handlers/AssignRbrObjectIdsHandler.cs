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
                    var fp = BuildFingerprintFromManualAssign(doc, element, SelectedMapping, levelCode);

                    var record = new ObjectIdLedgerRecord
                    {
                        ObjectId        = r.NewValue,
                        ElementUniqueId = element.UniqueId ?? string.Empty,
                        ElementId       = r.ElementId.Value,
                        RbrPrCode       = fp.RbrPrCode,
                        PbsPartR        = fp.PbsPartR,
                        PbsPartS        = fp.PbsPartS,
                        LevelCode       = fp.LevelCode,
                        Prefix          = fp.Prefix,
                        Category        = fp.Category,
                        FamilyName      = fp.FamilyName,
                        TypeId          = fp.TypeId,
                        TypeName        = fp.TypeName,
                        Fingerprint     = fp.Fingerprint,
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

        private static ObjectFingerprintInfo BuildFingerprintFromManualAssign(
            Document doc,
            Element element,
            PbsMapping selectedMapping,
            string levelCode)
        {
            var typeId = element.GetTypeId();
            long typeIdValue = (typeId != null && typeId != ElementId.InvalidElementId)
                ? typeId.Value : 0L;

            string typeName = typeIdValue != 0
                ? doc.GetElement(typeId)?.Name ?? string.Empty
                : string.Empty;

            var info = new ObjectFingerprintInfo
            {
                RbrPrCode  = string.Empty,
                PbsPartR   = selectedMapping?.DisciplineCode ?? string.Empty,
                PbsPartS   = selectedMapping?.ObjectCode ?? string.Empty,
                LevelCode  = levelCode ?? string.Empty,
                Prefix     = $"{selectedMapping?.DisciplineCode}-{selectedMapping?.ObjectCode}-{levelCode}",
                TypeId     = typeIdValue,
                TypeName   = typeName,
                Category   = element.Category?.Name ?? string.Empty,
                FamilyName = (element as FamilyInstance)?.Symbol?.FamilyName ?? string.Empty,
            };

            info.Fingerprint = RbrObjectFingerprintService.BuildFingerprintString(info);
            return info;
        }
    }
}
