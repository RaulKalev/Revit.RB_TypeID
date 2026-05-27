using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RB_TypeName.Handlers
{
    /// <summary>
    /// Takes a list of TypeNumberPreviewRows (populated by LoadRbrTypeGroupsHandler,
    /// with L1Codes optionally edited by the user) and generates stable proposed
    /// RBR-Type_number values. Does NOT write to the model.
    /// </summary>
    public class PreviewRbrTypeNumbersHandler : IExternalEventHandler
    {
        // ── Input — set from UI thread before Raise() ────────────────────────

        public List<TypeNumberPreviewRow> Rows         { get; set; }
        public string                     LedgerFolder { get; set; }

        // ── Output callback — dispatched to UI thread ─────────────────────────

        public Action<List<TypeNumberPreviewRow>, RbrTypeNumberIndex> OnCompleted { get; set; }

        // ── IExternalEventHandler ─────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            var rows  = Rows ?? new List<TypeNumberPreviewRow>();
            RbrTypeNumberIndex index = null;

            try
            {
                var doc = app.ActiveUIDocument.Document;
                index   = RbrTypeNumberIndex.BuildFromDocument(doc);

                string docPath    = doc.PathName ?? doc.Title ?? "default";
                string ledgerPath = RbrTypeNumberLedgerService.GetLedgerFilePath(
                    LedgerFolder, docPath);
                var ledger = new RbrTypeNumberLedgerService(ledgerPath);

                foreach (var row in rows)
                {
                    // Keep hard-error rows and already-assigned rows unchanged.
                    if (row.Status == "Already has Type Number"          ||
                        row.Status == "Missing RBR-Type_number parameter" ||
                        row.Status == "RBR-Type_number parameter read-only")
                        continue;

                    // Require a valid L1 code before generating a number.
                    string userL1 = (row.L1Code ?? string.Empty).Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(userL1))
                    {
                        row.Status             = "Missing L1 code";
                        row.ProposedTypeNumber = string.Empty;
                        row.IsSelected         = false;
                        continue;
                    }
                    if (!Regex.IsMatch(userL1, @"^[A-Z0-9_\-]+$"))
                    {
                        row.Status             = "Invalid L1 code (use A-Z, 0-9, - or _)";
                        row.ProposedTypeNumber = string.Empty;
                        row.IsSelected         = false;
                        continue;
                    }

                    // ── Determine prefix from user's L1Code (not from template) ──
                    // If a PBS template exists, extract the suffix after ZZZZ from it.
                    string template    = row.PbsTypeTemplate ?? string.Empty;
                    bool   hasTemplate = template.IndexOf("ZZZZ",
                        StringComparison.OrdinalIgnoreCase) >= 0;

                    // Prefix is ALWAYS the user's L1Code (trimmed, uppercase).
                    string prefix = userL1;

                    // ── Next number = max(model max, ledger max) + 1 ─────────
                    int indexMax  = index.GetMaxNumber(prefix);
                    int ledgerMax = ledger.GetLastIssuedNumber(prefix);
                    int nextNum   = Math.Max(indexMax, ledgerMax) + 1;

                    // ── Build proposed value ─────────────────────────────────
                    string proposed;
                    if (hasTemplate)
                    {
                        // Take only the part after ZZZZ from the template as suffix.
                        int    zzIdx     = template.IndexOf("ZZZZ",
                            StringComparison.OrdinalIgnoreCase);
                        string afterZzzz = template.Substring(zzIdx + 4);
                        proposed = prefix + nextNum.ToString("D4") + afterZzzz;
                    }
                    else
                    {
                        // No template: prefix + "-" + 4-digit number.
                        string sep = prefix.EndsWith("-") ? string.Empty : "-";
                        proposed = prefix + sep + nextNum.ToString("D4");
                    }

                    // Reserve in-memory so the next row in this batch increments.
                    index.Register(proposed, prefix, nextNum);

                    row.ProposedTypeNumber = proposed;
                    row.Status             = "Ready";
                    row.IsSelected         = true;
                }
            }
            catch (Exception ex)
            {
                rows.Add(new TypeNumberPreviewRow
                {
                    Status  = "Error",
                    Message = ex.Message,
                });
            }

            OnCompleted?.Invoke(rows, index);
        }

        public string GetName() => "Preview RBR Type Numbers";
    }
}

