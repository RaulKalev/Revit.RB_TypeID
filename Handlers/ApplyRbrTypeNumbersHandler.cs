using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RB_TypeName.Handlers
{
    /// <summary>
    /// Writes proposed RBR-Type_number values from a completed preview to Revit ElementTypes.
    /// Only rows where IsSelected == true and Status == "Ready" are written.
    /// Re-validates before writing and persists the ledger after a successful transaction.
    /// </summary>
    public class ApplyRbrTypeNumbersHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────

        public List<TypeNumberPreviewRow> RowsToApply { get; set; }
        public string                     LedgerFolder { get; set; }

        /// <summary>Current TypeSource setting — persisted to Extensible Storage during apply.</summary>
        public string SelectedTypeSourceParameterName { get; set; } = "Revit Type Name";

        /// <summary>Current Discipline setting — persisted to Extensible Storage during apply.</summary>
        public string SelectedDisciplineCode { get; set; } = string.Empty;

        // ── Output callback — dispatched to UI thread ─────────────────────────

        public Action<int, int, List<string>> OnCompleted { get; set; }

        // ── IExternalEventHandler ─────────────────────────────────────────────

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
                    ?? new List<TypeNumberPreviewRow>();

                if (toWrite.Count == 0)
                {
                    OnCompleted?.Invoke(0, 0, errors);
                    return;
                }

                // Build fresh index to catch changes since preview.
                var index = RbrTypeNumberIndex.BuildFromDocument(doc);

                string docPath    = doc.PathName ?? doc.Title ?? "default";
                string ledgerPath = RbrTypeNumberLedgerService.GetLedgerFilePath(
                    LedgerFolder, docPath);
                var ledger = new RbrTypeNumberLedgerService(ledgerPath);

                using var tx = new Transaction(doc, "Apply RBR Type Numbers");
                tx.Start();

                foreach (var row in toWrite)
                {
                    var elemType = doc.GetElement(row.ElementTypeId) as ElementType;
                    if (elemType == null)
                    {
                        row.Status  = "Failed";
                        row.Message = "Element type no longer exists.";
                        errors.Add($"Type {row.TypeName}: not found in document.");
                        continue;
                    }

                    var param = RevitParameterResolver.FindTypeNumberParameter(elemType);
                    if (param == null)
                    {
                        row.Status  = "Failed";
                        row.Message = "Type number parameter no longer available.";
                        errors.Add($"Type {row.TypeName}: parameter missing.");
                        continue;
                    }

                    if (param.IsReadOnly)
                    {
                        row.Status  = "Failed";
                        row.Message = "Parameter is read-only.";
                        errors.Add($"Type {row.TypeName}: parameter read-only.");
                        continue;
                    }

                    string existing = param.AsString();
                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        row.Status  = "Skipped";
                        row.Message = "Type number was assigned since preview was built.";
                        skipped++;
                        continue;
                    }

                    if (index.Contains(row.ProposedTypeNumber))
                    {
                        row.Status  = "Failed";
                        row.Message = $"'{row.ProposedTypeNumber}' is a duplicate since preview.";
                        errors.Add($"Type {row.TypeName}: duplicate '{row.ProposedTypeNumber}'.");
                        continue;
                    }

                    param.Set(row.ProposedTypeNumber);

                    string prefix = GetPrefix(row);
                    int    num    = GetGeneratedNumber(row);
                    index.Register(row.ProposedTypeNumber, prefix, num);
                    if (!string.IsNullOrWhiteSpace(prefix) && num > 0)
                        ledger.UpdateLastIssuedNumber(prefix, num);

                    row.Status = "Assigned";
                    assigned++;
                }

                // Persist current UI settings alongside the apply transaction.
                TypeNumberSettingsStorageService.Save(doc, new TypeNumberSettings
                {
                    SelectedTypeSourceParameterName = SelectedTypeSourceParameterName,
                    SelectedDisciplineCode          = SelectedDisciplineCode,
                });

                tx.Commit();

                // Persist ledger only after a successful commit.
                ledger.Save();
            }
            catch (Exception ex)
            {
                errors.Add("Unhandled error during apply: " + ex.Message);
            }

            OnCompleted?.Invoke(assigned, skipped, errors);
        }

        public string GetName() => "Apply RBR Type Numbers";

        // ── Helpers ──────────────────────────────────────────────────────────

        // Prefix mirrors PreviewRbrTypeNumbersHandler: always the user/saved L1Code,
        // trimmed and upper-cased. The PBS template is only used for the suffix after
        // ZZZZ, never for the ledger prefix.
        private static string GetPrefix(TypeNumberPreviewRow row)
        {
            string l1 = (row.L1Code ?? string.Empty).Trim().ToUpperInvariant();
            return string.IsNullOrWhiteSpace(l1) ? string.Empty : l1;
        }

        // Extracts the 4-digit running number that appears immediately after the L1
        // prefix in the proposed value. Supports values with a suffix after the digits
        // (e.g. "CAM-020001-X" -> 1).
        private static int GetGeneratedNumber(TypeNumberPreviewRow row)
        {
            string proposed = row.ProposedTypeNumber ?? string.Empty;
            string prefix   = GetPrefix(row);

            if (string.IsNullOrWhiteSpace(proposed) || string.IsNullOrWhiteSpace(prefix))
                return 0;

            if (!proposed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return 0;

            // Preview inserts a "-" separator when prefix doesn't already end with one
            // and no PBS template is present. Skip it here so the digit match succeeds.
            string rest = proposed.Substring(prefix.Length);
            if (rest.StartsWith("-", StringComparison.Ordinal))
                rest = rest.Substring(1);

            var m = Regex.Match(rest, @"^(\d{4})");
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
        }
    }
}
