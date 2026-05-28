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
    /// Writes proposed Object IDs from a completed reconcile preview to Revit elements.
    /// Only rows where IsSelected == true and IsWriteAllowed == true are written.
    /// Re-validates each row immediately before writing.
    /// Updates the Object ID ledger in Extensible Storage within the same transaction.
    /// </summary>
    public class ApplyRbrObjectIdReconcileHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────
        public List<ObjectIdReconcileRow> RowsToApply  { get; set; }
        public ObjectIdReconcileOptions   Options       { get; set; }
        public PbsPrCodeLookupService     LookupService { get; set; }

        // ── Output callback — dispatched to UI thread ────────────────────────
        /// <summary>Callback(applied, skipped, errorMessages)</summary>
        public Action<int, int, List<string>> OnCompleted { get; set; }

        // ── IExternalEventHandler ────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            int applied  = 0;
            int skipped  = 0;
            var errors   = new List<string>();

            try
            {
                var doc = app.ActiveUIDocument.Document;

                var toWrite = RowsToApply?
                    .Where(r => r.IsSelected && r.IsWriteAllowed
                                && !string.IsNullOrWhiteSpace(r.ProposedObjectId))
                    .ToList()
                    ?? new List<ObjectIdReconcileRow>();

                if (toWrite.Count == 0)
                {
                    OnCompleted?.Invoke(0, 0, errors);
                    return;
                }

                // Build a fresh index to catch anything that changed since the preview.
                var currentIndex = RbrObjectIdIndex.BuildFromDocument(doc);

                // Load ledger for updates.
                var ledger = RbrObjectIdLedgerService.Load(doc);

                using var tx = new Transaction(doc, "Apply RBR Object ID Reconcile");
                tx.Start();

                foreach (var row in toWrite)
                {
                    var element = doc.GetElement(row.ElementId);
                    if (element == null)
                    {
                        row.Status = "Failed";
                        row.Reason = "Element no longer exists.";
                        errors.Add($"Element {row.ElementIdValue}: not found.");
                        skipped++;
                        continue;
                    }

                    var param = RevitParameterResolver.FindObjectIdParameter(element);
                    if (param == null)
                    {
                        row.Status = "Failed";
                        row.Reason = "Object ID parameter missing.";
                        errors.Add($"Element {row.ElementIdValue}: parameter missing.");
                        skipped++;
                        continue;
                    }

                    if (param.IsReadOnly)
                    {
                        row.Status = "Failed";
                        row.Reason = "Parameter is read-only.";
                        errors.Add($"Element {row.ElementIdValue}: parameter read-only.");
                        skipped++;
                        continue;
                    }

                    // Re-check current value hasn't changed since preview.
                    string liveValue = param.AsString() ?? string.Empty;
                    bool wasEmpty  = string.IsNullOrWhiteSpace(row.CurrentObjectId);
                    bool liveEmpty = string.IsNullOrWhiteSpace(liveValue);

                    if (!wasEmpty && !string.Equals(liveValue, row.CurrentObjectId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        row.Status = "Skipped";
                        row.Reason = "Object ID changed since preview was built.";
                        skipped++;
                        continue;
                    }

                    if (wasEmpty && !liveEmpty)
                    {
                        row.Status = "Skipped";
                        row.Reason = "Object ID was set since preview was built.";
                        skipped++;
                        continue;
                    }

                    // Re-check the proposed ID is still available.
                    if (currentIndex.Contains(row.ProposedObjectId)
                        && !string.Equals(row.ProposedObjectId, row.CurrentObjectId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        row.Status = "Failed";
                        row.Reason = $"'{row.ProposedObjectId}' is already taken.";
                        errors.Add($"Element {row.ElementIdValue}: proposed ID '{row.ProposedObjectId}' duplicate.");
                        skipped++;
                        continue;
                    }

                    // ── Write ────────────────────────────────────────────────
                    param.Set(row.ProposedObjectId);
                    currentIndex.Register(row.ProposedObjectId);
                    row.Status = "Applied";
                    applied++;

                    // ── Update ledger ────────────────────────────────────────
                    UpdateLedger(doc, element, row, ledger, LookupService);
                }

                // Save ledger within the same transaction.
                RbrObjectIdLedgerService.Save(doc, ledger);

                tx.Commit();
            }
            catch (Exception ex)
            {
                errors.Add("Unhandled error during apply: " + ex.Message);
            }

            OnCompleted?.Invoke(applied, skipped, errors);
        }

        public string GetName() => "Apply RBR Object ID Reconcile";

        // ── Ledger update ─────────────────────────────────────────────────────

        private static void UpdateLedger(
            Document doc,
            Element element,
            ObjectIdReconcileRow row,
            ObjectIdLedgerData ledger,
            PbsPrCodeLookupService lookupService)
        {
            bool isReassignment =
                !string.IsNullOrWhiteSpace(row.CurrentObjectId)
                && !string.Equals(row.CurrentObjectId, row.ProposedObjectId,
                    StringComparison.OrdinalIgnoreCase);

            var existingRecord = RbrObjectIdLedgerService.FindByUniqueId(ledger, element.UniqueId);
            var previousIds = existingRecord?.PreviousObjectIds ?? new List<string>();

            // If reassigning, retire the old ID and track it in history.
            if (isReassignment)
            {
                // Only retire if this element was the keeper/sole owner of the old ID.
                bool isSoleOwnerOfOldId = string.Equals(
                    existingRecord?.ObjectId, row.CurrentObjectId,
                    StringComparison.OrdinalIgnoreCase);

                if (isSoleOwnerOfOldId)
                    RbrObjectIdLedgerService.RetireObjectId(ledger, row.CurrentObjectId);

                if (!previousIds.Contains(row.CurrentObjectId, StringComparer.OrdinalIgnoreCase))
                    previousIds.Add(row.CurrentObjectId);
            }

            // Build a fresh fingerprint from the live element.
            var fp = RbrObjectFingerprintService.Build(doc, element, lookupService);

            // Fallback: if fingerprint service couldn't resolve prefix, use row data.
            if (string.IsNullOrWhiteSpace(fp.Prefix))
            {
                fp.Prefix    = row.ExpectedPrefix;
                fp.PbsPartR  = ExtractPbsR(row.ExpectedPrefix);
                fp.PbsPartS  = ExtractPbsS(row.ExpectedPrefix);
                fp.LevelCode = row.LevelCode;
                fp.Fingerprint = RbrObjectFingerprintService.BuildFingerprintString(fp);
            }

            var record = new ObjectIdLedgerRecord
            {
                ObjectId        = row.ProposedObjectId,
                ElementUniqueId = element.UniqueId ?? string.Empty,
                ElementId       = element.Id.Value,
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
                AssignedUtc     = existingRecord?.AssignedUtc ?? DateTime.UtcNow.ToString("o"),
                UpdatedUtc      = DateTime.UtcNow.ToString("o"),
                PreviousObjectIds = previousIds,
            };

            RbrObjectIdLedgerService.UpsertRecord(ledger, record);
        }

        private static string ExtractPbsR(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
            var parts = prefix.Split('-');
            return parts.Length >= 1 ? parts[0] : string.Empty;
        }

        private static string ExtractPbsS(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
            var parts = prefix.Split('-');
            return parts.Length >= 2 ? parts[1] : string.Empty;
        }
    }
}
