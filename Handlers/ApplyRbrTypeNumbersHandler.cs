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
                    int    num    = GetTrailingNumber(row.ProposedTypeNumber);
                    index.Register(row.ProposedTypeNumber, prefix, num);
                    if (!string.IsNullOrWhiteSpace(prefix))
                        ledger.UpdateLastIssuedNumber(prefix, num);

                    row.Status = "Assigned";
                    assigned++;
                }

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

        private static string GetPrefix(TypeNumberPreviewRow row)
        {
            string template = row.PbsTypeTemplate ?? string.Empty;
            if (template.IndexOf("ZZZZ", StringComparison.OrdinalIgnoreCase) >= 0)
                return template.Substring(0,
                    template.IndexOf("ZZZZ", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(row.L1Code))
                return row.L1Code.TrimEnd('-') + "-";

            return string.Empty;
        }

        private static int GetTrailingNumber(string proposed)
        {
            if (string.IsNullOrWhiteSpace(proposed)) return 0;
            var m = Regex.Match(proposed, @"(\d{4})$");
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
        }
    }
}
