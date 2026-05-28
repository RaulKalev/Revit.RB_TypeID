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
    /// Writes the proposed RBR-Object_IDs from a completed preview to Revit elements.
    /// Only rows where IsSelected == true and Status == "Ready" are written.
    /// Re-validates each element immediately before writing.
    /// </summary>
    public class ApplyRbrObjectIdsHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────
        public List<ObjectIdPreviewRow> RowsToApply { get; set; }

        // ── Output callback — dispatched to UI thread ────────────────────────
        public Action<int, int, List<string>> OnCompleted { get; set; }

        // ── IExternalEventHandler ────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            int assigned = 0;
            int skipped  = 0;
            var errors   = new List<string>();

            try
            {
                var doc = app.ActiveUIDocument.Document;

                var toWrite = RowsToApply?
                    .Where(r => r.IsSelected && r.IsReady)
                    .ToList()
                    ?? new List<ObjectIdPreviewRow>();

                if (toWrite.Count == 0)
                {
                    OnCompleted?.Invoke(0, 0, errors);
                    return;
                }

                // Build a fresh index to catch changes since the preview was built.
                var currentIndex = RbrObjectIdIndex.BuildFromDocument(doc);
                var ledger = RbrObjectIdLedgerService.Load(doc);
                var now    = DateTime.UtcNow.ToString("o");

                using var tx = new Transaction(doc, "Apply RBR Object IDs");
                tx.Start();

                foreach (var row in toWrite)
                {
                    var element = doc.GetElement(row.ElementId);
                    if (element == null)
                    {
                        row.Status  = "Failed";
                        row.Message = "Element no longer exists.";
                        errors.Add($"Element {row.ElementIdValue}: not found in document.");
                        continue;
                    }

                    var param = RevitParameterResolver.FindObjectIdParameter(element);
                    if (param == null)
                    {
                        row.Status  = "Failed";
                        row.Message = "Object ID parameter no longer available.";
                        errors.Add($"Element {row.ElementIdValue}: parameter missing.");
                        continue;
                    }

                    if (param.IsReadOnly)
                    {
                        row.Status  = "Failed";
                        row.Message = "Parameter is read-only.";
                        errors.Add($"Element {row.ElementIdValue}: parameter read-only.");
                        continue;
                    }

                    string existing = param.AsString();
                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        row.Status  = "Skipped";
                        row.Message = "ID was set since preview was built.";
                        skipped++;
                        continue;
                    }

                    // Check the proposed ID has not become a duplicate since preview.
                    if (currentIndex.Contains(row.ProposedObjectId))
                    {
                        row.Status  = "Failed";
                        row.Message = $"'{row.ProposedObjectId}' is a duplicate since preview.";
                        errors.Add($"Element {row.ElementIdValue}: duplicate '{row.ProposedObjectId}'.");
                        continue;
                    }

                    param.Set(row.ProposedObjectId);
                    currentIndex.Register(row.ProposedObjectId);
                    row.Status = "Assigned";
                    assigned++;

                    // Record in ledger.
                    var typeEid = element.GetTypeId();
                    var record = new ObjectIdLedgerRecord
                    {
                        ObjectId        = row.ProposedObjectId,
                        ElementUniqueId = element.UniqueId ?? string.Empty,
                        ElementId       = row.ElementIdValue,
                        RbrPrCode       = row.RbrPrCode,
                        PbsPartR        = row.PbsPartR,
                        PbsPartS        = row.PbsPartS,
                        LevelCode       = row.LevelCode,
                        Prefix          = $"{row.PbsPartR}-{row.PbsPartS}-{row.LevelCode}",
                        Category        = row.Category,
                        FamilyName      = (element as FamilyInstance)?.Symbol?.FamilyName ?? string.Empty,
                        TypeId          = (typeEid != null && typeEid != ElementId.InvalidElementId)
                                           ? typeEid.Value : 0L,
                        TypeName        = doc.GetElement(typeEid)?.Name ?? string.Empty,
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
                errors.Add("Unhandled error during apply: " + ex.Message);
            }

            OnCompleted?.Invoke(assigned, skipped, errors);
        }

        public string GetName() => "Apply RBR Object IDs";
    }
}
