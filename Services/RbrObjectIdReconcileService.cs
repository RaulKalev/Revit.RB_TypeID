using Autodesk.Revit.DB;
using RB_TypeName.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Builds a reconcile preview: for each selected element it determines whether
    /// its Object ID is valid, missing, duplicated, or stale (fingerprint changed).
    ///
    /// This service is read-only — it never modifies the Revit model.
    /// </summary>
    public static class RbrObjectIdReconcileService
    {
        // ── Status constants ─────────────────────────────────────────────────

        public const string StatusValid               = "Valid";
        public const string StatusMissing             = "Missing Object ID";
        public const string StatusMalformed           = "Malformed Object ID";
        public const string StatusDuplicateKeeper     = "Duplicate Object ID — keeper";
        public const string StatusDuplicateNeedsNew   = "Duplicate Object ID — needs new ID";
        public const string StatusCopied              = "Copied element — inherited ID";
        public const string StatusAmbiguous           = "Ambiguous duplicate";
        public const string StatusChangedType         = "Changed Type / Classification";
        public const string StatusChangedLevel        = "Changed Level";
        public const string StatusPrefixMismatch      = "Prefix mismatch";
        public const string StatusMissingParam        = "Missing RBR-Object_ID parameter";
        public const string StatusParamReadOnly       = "RBR-Object_ID parameter read-only";
        public const string StatusMissingPrCode       = "Missing RBR_Pr_Code";
        public const string StatusNoPbsMatch          = "No PBS match";
        public const string StatusMissingLevel        = "Missing level";
        public const string StatusError               = "Error";

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds the full reconcile preview for the given selected elements.
        /// Scans the whole document for duplicate detection but only returns rows
        /// for the <paramref name="selectedIds"/> set.
        /// </summary>
        public static ObjectIdReconcileResult BuildPreview(
            Document doc,
            IEnumerable<ElementId> selectedIds,
            PbsPrCodeLookupService lookupService,
            ObjectIdReconcileOptions options)
        {
            var result = new ObjectIdReconcileResult();

            // 1. Load ledger
            var ledger = RbrObjectIdLedgerService.Load(doc);

            // 2. Build model-wide map: objectId → all element IDs that carry that ID
            var modelIdMap = BuildModelIdMap(doc);

            // 3. Build the reserved index (model IDs + ledger reserved + retired)
            var index = BuildReservedIndex(doc, ledger);

            // 4. Process each selected element
            var rows = new List<ObjectIdReconcileRow>();
            var idList = selectedIds?.OrderBy(id => id.Value).ToList()
                         ?? new List<ElementId>();

            foreach (var elementId in idList)
            {
                var element = doc.GetElement(elementId);
                if (element == null) continue;

                try
                {
                    var row = BuildRow(doc, element, lookupService, ledger, modelIdMap, options);
                    rows.Add(row);
                }
                catch (Exception ex)
                {
                    rows.Add(new ObjectIdReconcileRow
                    {
                        ElementId  = elementId,
                        UniqueId   = GetSafeUniqueId(doc, elementId),
                        Status     = StatusError,
                        Reason     = ex.Message,
                        IsSelected = false,
                    });
                }
            }

            // 5. Generate proposed IDs for rows that need them
            GenerateProposedIds(rows, index, options);

            // 6. Apply option filters and selection defaults
            FinalizeRows(rows, options);

            // 7. Build filtered output
            result.Rows = options.IncludeValidRows
                ? rows
                : rows.Where(r => r.Status != StatusValid).ToList();

            // 8. Summary counts (always over all rows, not just filtered)
            result.ValidCount     = rows.Count(r => r.Status == StatusValid);
            result.MissingCount   = rows.Count(r => r.Status == StatusMissing);
            result.DuplicateCount = rows.Count(r =>
                r.Status == StatusDuplicateNeedsNew ||
                r.Status == StatusCopied ||
                r.Status == StatusAmbiguous);
            result.ChangedCount   = rows.Count(r =>
                r.Status == StatusChangedType  ||
                r.Status == StatusChangedLevel ||
                r.Status == StatusPrefixMismatch);
            result.ErrorCount     = rows.Count(r =>
                r.Status == StatusError         ||
                r.Status == StatusMissingParam  ||
                r.Status == StatusParamReadOnly ||
                r.Status == StatusMissingPrCode ||
                r.Status == StatusNoPbsMatch    ||
                r.Status == StatusMissingLevel);

            return result;
        }

        // ── Row building ─────────────────────────────────────────────────────

        private static ObjectIdReconcileRow BuildRow(
            Document doc,
            Element element,
            PbsPrCodeLookupService lookupService,
            ObjectIdLedgerData ledger,
            Dictionary<string, List<ElementId>> modelIdMap,
            ObjectIdReconcileOptions options)
        {
            var row = new ObjectIdReconcileRow
            {
                ElementId  = element.Id,
                UniqueId   = element.UniqueId ?? string.Empty,
                Category   = element.Category?.Name ?? string.Empty,
                FamilyName = (element as FamilyInstance)?.Symbol?.FamilyName ?? string.Empty,
                TypeName   = GetTypeName(element, doc),
            };

            var typeId = element.GetTypeId();
            row.TypeIdValue = (typeId != null && typeId != ElementId.InvalidElementId)
                ? typeId.Value : 0;

            // ── Parameter check ──────────────────────────────────────────────
            var objIdParam = RevitParameterResolver.FindObjectIdParameter(element);
            if (objIdParam == null)
            {
                row.Status = StatusMissingParam;
                row.IsSelected = false;
                return row;
            }
            if (objIdParam.IsReadOnly)
            {
                row.Status = StatusParamReadOnly;
                row.IsSelected = false;
                return row;
            }

            row.CurrentObjectId = objIdParam.AsString() ?? string.Empty;

            // ── Resolve PBS parts ────────────────────────────────────────────
            string prCode = RevitParameterResolver.ReadPrCode(element);
            row.RbrPrCode = prCode ?? string.Empty;

            string pbsR = string.Empty, pbsS = string.Empty;
            if (!string.IsNullOrWhiteSpace(prCode) && lookupService != null)
            {
                var lookup = lookupService.FindByPrCode(prCode);
                if (lookup.Success && lookup.Row != null)
                {
                    pbsR = lookup.Row.ObjectIdPart1 ?? string.Empty;
                    pbsS = lookup.Row.ObjectIdPart2 ?? string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(row.CurrentObjectId))
                {
                    // Has existing ID but lookup fails — note the issue but don't block
                    row.Reason = lookup.Status;
                }
                else
                {
                    row.Status = string.IsNullOrWhiteSpace(lookup.Status)
                        ? StatusNoPbsMatch : lookup.Status;
                    row.Reason = lookup.Message ?? string.Empty;
                    row.IsSelected = false;
                    return row;
                }
            }
            else if (string.IsNullOrWhiteSpace(prCode)
                     && string.IsNullOrWhiteSpace(row.CurrentObjectId))
            {
                row.Status = StatusMissingPrCode;
                row.IsSelected = false;
                return row;
            }

            // ── Level ────────────────────────────────────────────────────────
            bool hasLevel = LevelCodeService.TryGetLevelCode(element, doc, out string levelCode);
            row.LevelCode = levelCode ?? string.Empty;

            if (!hasLevel && string.IsNullOrWhiteSpace(row.CurrentObjectId))
            {
                row.Status = StatusMissingLevel;
                row.IsSelected = false;
                return row;
            }

            // Compute expected prefix (may be empty if PBS parts unavailable)
            if (!string.IsNullOrWhiteSpace(pbsR)
                && !string.IsNullOrWhiteSpace(pbsS)
                && !string.IsNullOrWhiteSpace(levelCode))
            {
                row.ExpectedPrefix = $"{pbsR}-{pbsS}-{levelCode}";
            }

            // ── No current Object ID ─────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(row.CurrentObjectId))
            {
                row.Status = StatusMissing;
                row.IsWriteAllowed = options.IncludeMissingIds;
                return row;
            }

            // ── Duplicate detection ──────────────────────────────────────────
            if (modelIdMap.TryGetValue(row.CurrentObjectId, out var sharedBy)
                && sharedBy.Count > 1)
            {
                ResolveDuplicateStatus(doc, row, element, sharedBy, ledger, options);
                // CurrentPrefix from existing ID
                if (RbrObjectIdParser.TryParse(row.CurrentObjectId, out var parsed))
                    row.CurrentPrefix = parsed.Prefix;
                return row;
            }

            // ── Malformed ID ─────────────────────────────────────────────────
            if (!RbrObjectIdParser.TryParse(row.CurrentObjectId, out var parsedId))
            {
                row.Status     = StatusMalformed;
                row.Reason     = "Object ID does not match expected format (R-S-LLL-0000).";
                row.IsWriteAllowed = options.RepairDuplicates;
                return row;
            }

            row.CurrentPrefix = parsedId.Prefix;

            // ── Prefix mismatch ──────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(row.ExpectedPrefix)
                && !string.Equals(parsedId.Prefix, row.ExpectedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                row.Status     = StatusPrefixMismatch;
                row.Reason     = $"Current prefix '{parsedId.Prefix}' does not match expected '{row.ExpectedPrefix}'.";
                row.IsWriteAllowed = options.ReassignChangedElements;
                return row;
            }

            // ── Fingerprint check ────────────────────────────────────────────
            var ledgerRecord = RbrObjectIdLedgerService.FindByUniqueId(ledger, element.UniqueId);
            if (ledgerRecord != null && options.FlagChangedElements)
            {
                var fp = RbrObjectFingerprintService.Build(doc, element, lookupService);
                row.Fingerprint = fp.Fingerprint;

                var changeReasons = RbrObjectFingerprintService.Compare(ledgerRecord, fp);
                if (changeReasons.Count > 0)
                {
                    bool levelOnly = changeReasons.All(r => r.StartsWith("LevelCode:"));
                    row.Status        = levelOnly ? StatusChangedLevel : StatusChangedType;
                    row.ChangeReasons = changeReasons;
                    row.Reason        = string.Join("; ", changeReasons);
                    row.StoredFingerprint  = ledgerRecord.Fingerprint;
                    row.PreviousTypeName   = ledgerRecord.TypeName;
                    row.PreviousRbrPrCode  = ledgerRecord.RbrPrCode;
                    row.PreviousLevelCode  = ledgerRecord.LevelCode;
                    row.IsWriteAllowed     = options.ReassignChangedElements;
                    return row;
                }
            }

            // ── Valid ────────────────────────────────────────────────────────
            row.Status     = StatusValid;
            row.IsWriteAllowed = false;
            return row;
        }

        // ── Duplicate resolution ─────────────────────────────────────────────

        private static void ResolveDuplicateStatus(
            Document doc,
            ObjectIdReconcileRow row,
            Element element,
            List<ElementId> sharedBy,
            ObjectIdLedgerData ledger,
            ObjectIdReconcileOptions options)
        {
            // Find the ledger record for this Object ID
            var ledgerRecord = RbrObjectIdLedgerService.FindByObjectId(ledger, row.CurrentObjectId);

            if (ledgerRecord != null)
            {
                // Find elements whose UniqueId matches the ledger owner
                var owners = sharedBy.Where(id =>
                {
                    var e = doc.GetElement(id);
                    return e != null && string.Equals(
                        e.UniqueId, ledgerRecord.ElementUniqueId,
                        StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (owners.Count == 1)
                {
                    if (owners[0] == element.Id)
                    {
                        row.Status = StatusDuplicateKeeper;
                        row.Reason = $"Ledger owner; {sharedBy.Count} elements share this ID.";
                        row.IsWriteAllowed = false;
                    }
                    else
                    {
                        row.Status = StatusCopied;
                        row.Reason = $"Ledger owner is element {owners[0].Value}.";
                        row.IsWriteAllowed = options.RepairDuplicates;
                    }
                    return;
                }

                if (owners.Count > 1)
                {
                    row.Status = StatusAmbiguous;
                    row.Reason = "Multiple elements claim ledger ownership. Manual resolution required.";
                    row.IsWriteAllowed = false;
                    return;
                }
            }

            // No ledger owner found — fall back to lowest ElementId
            var lowest = sharedBy.OrderBy(id => id.Value).First();
            if (lowest == element.Id)
            {
                row.Status = StatusDuplicateKeeper;
                row.Reason = $"Keeper: lowest ElementId (no ledger owner found; {sharedBy.Count} total).";
                row.IsWriteAllowed = false;
            }
            else
            {
                row.Status = StatusDuplicateNeedsNew;
                row.Reason = "Keeper selected by lowest ElementId because no ledger owner was found.";
                row.IsWriteAllowed = options.RepairDuplicates;
            }
        }

        // ── Proposed ID generation ───────────────────────────────────────────

        private static void GenerateProposedIds(
            List<ObjectIdReconcileRow> rows,
            RbrObjectIdIndex index,
            ObjectIdReconcileOptions options)
        {
            foreach (var row in rows)
            {
                if (!row.IsWriteAllowed) continue;
                if (!NeedsProposedId(row, options)) continue;
                if (string.IsNullOrWhiteSpace(row.ExpectedPrefix)) continue;

                int next     = index.GetNextNumber(row.ExpectedPrefix);
                string newId = $"{row.ExpectedPrefix}-{next:D4}";
                index.Register(newId);
                row.ProposedObjectId = newId;
            }
        }

        private static bool NeedsProposedId(ObjectIdReconcileRow row, ObjectIdReconcileOptions options)
        {
            return row.Status == StatusMissing
                || row.Status == StatusDuplicateNeedsNew
                || row.Status == StatusCopied
                || row.Status == StatusMalformed
                || row.Status == StatusPrefixMismatch
                || ((row.Status == StatusChangedType || row.Status == StatusChangedLevel)
                    && options.ReassignChangedElements);
        }

        // ── Finalize ─────────────────────────────────────────────────────────

        private static void FinalizeRows(List<ObjectIdReconcileRow> rows,
                                          ObjectIdReconcileOptions options)
        {
            foreach (var row in rows)
            {
                // Default selection: only select rows that are write-allowed AND have a proposed ID
                if (row.IsWriteAllowed && !string.IsNullOrWhiteSpace(row.ProposedObjectId))
                    row.IsSelected = true;
                else
                    row.IsSelected = false;
            }
        }

        // ── Index building ───────────────────────────────────────────────────

        /// <summary>
        /// Builds a running-number index that includes all current model IDs,
        /// all ledger-recorded IDs, and all retired IDs so proposed numbers
        /// never collide with past or present assignments.
        /// </summary>
        private static RbrObjectIdIndex BuildReservedIndex(
            Document doc, ObjectIdLedgerData ledger)
        {
            // Start from the model scan (existing numbers)
            var index = RbrObjectIdIndex.BuildFromDocument(doc);

            // Also register all ledger records and retired IDs so gaps are never filled
            var reserved = RbrObjectIdLedgerService.GetReservedObjectIds(ledger);
            foreach (var id in reserved)
                index.Register(id);

            return index;
        }

        /// <summary>
        /// Scans the whole document and returns a map of Object ID → elements that carry it.
        /// Only IDs present on more than one element are useful for duplicate detection,
        /// but we return all of them for completeness.
        /// </summary>
        private static Dictionary<string, List<ElementId>> BuildModelIdMap(Document doc)
        {
            var map = new Dictionary<string, List<ElementId>>(
                StringComparer.OrdinalIgnoreCase);

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                var p = RevitParameterResolver.FindObjectIdParameter(element);
                if (p == null) continue;
                string val = p.AsString();
                if (string.IsNullOrWhiteSpace(val)) continue;
                val = val.Trim();
                if (!map.TryGetValue(val, out var list))
                    map[val] = list = new List<ElementId>();
                list.Add(element.Id);
            }

            return map;
        }

        // ── Misc helpers ─────────────────────────────────────────────────────

        private static string GetTypeName(Element element, Document doc)
        {
            var typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId)
                return string.Empty;
            return doc.GetElement(typeId)?.Name ?? string.Empty;
        }

        private static string GetSafeUniqueId(Document doc, ElementId id)
        {
            try { return doc.GetElement(id)?.UniqueId ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
