using Autodesk.Revit.DB;
using RB_TypeName.Models;
using System;
using System.Collections.Generic;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Builds and compares element identity fingerprints used to detect
    /// whether an element's classification has changed since its Object ID was assigned.
    /// </summary>
    public static class RbrObjectFingerprintService
    {
        /// <summary>
        /// Builds a fingerprint from the current state of <paramref name="element"/>.
        /// Uses <paramref name="lookupService"/> to resolve PBS R/S parts from RBR_Pr_Code.
        /// </summary>
        public static ObjectFingerprintInfo Build(
            Document doc,
            Element element,
            PbsPrCodeLookupService lookupService)
        {
            var info = new ObjectFingerprintInfo();

            // ── Category / Family / Type ─────────────────────────────────────
            info.Category   = element.Category?.Name ?? string.Empty;
            info.FamilyName = (element as FamilyInstance)?.Symbol?.FamilyName ?? string.Empty;
            info.TypeName   = GetTypeName(element, doc);

            var typeId = element.GetTypeId();
            info.TypeId = (typeId != null && typeId != ElementId.InvalidElementId)
                ? typeId.Value : 0;

            // ── RBR_Pr_Code ──────────────────────────────────────────────────
            string prCode = RevitParameterResolver.ReadPrCode(element) ?? string.Empty;
            info.RbrPrCode = prCode;

            // ── PBS R / S ────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(prCode) && lookupService != null)
            {
                var result = lookupService.FindByPrCode(prCode);
                if (result.Success && result.Row != null)
                {
                    info.PbsPartR = result.Row.ObjectIdPart1 ?? string.Empty;
                    info.PbsPartS = result.Row.ObjectIdPart2 ?? string.Empty;
                }
            }

            // ── Level ────────────────────────────────────────────────────────
            if (LevelCodeService.TryGetLevelCode(element, doc, out string levelCode))
                info.LevelCode = levelCode ?? string.Empty;

            // ── Prefix ──────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(info.PbsPartR)
                && !string.IsNullOrWhiteSpace(info.PbsPartS)
                && !string.IsNullOrWhiteSpace(info.LevelCode))
            {
                info.Prefix = $"{info.PbsPartR}-{info.PbsPartS}-{info.LevelCode}";
            }

            // ── Fingerprint string ───────────────────────────────────────────
            info.Fingerprint = BuildFingerprintString(info);

            return info;
        }

        /// <summary>
        /// Compares a stored ledger record to a freshly built fingerprint.
        /// Returns a list of human-readable change descriptions (empty if unchanged).
        /// </summary>
        public static List<string> Compare(
            Models.ObjectIdLedgerRecord stored,
            ObjectFingerprintInfo current)
        {
            var reasons = new List<string>();
            if (stored == null || current == null) return reasons;

            Check(reasons, "RbrPrCode",   stored.RbrPrCode,   current.RbrPrCode);
            Check(reasons, "PbsPartR",    stored.PbsPartR,    current.PbsPartR);
            Check(reasons, "PbsPartS",    stored.PbsPartS,    current.PbsPartS);
            Check(reasons, "LevelCode",   stored.LevelCode,   current.LevelCode);
            Check(reasons, "TypeId",      stored.TypeId.ToString(), current.TypeId.ToString());
            Check(reasons, "TypeName",    stored.TypeName,    current.TypeName);
            Check(reasons, "Category",    stored.Category,    current.Category);
            Check(reasons, "FamilyName",  stored.FamilyName,  current.FamilyName);

            return reasons;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static string BuildFingerprintString(ObjectFingerprintInfo info)
            => string.Join("|",
                Norm(info.RbrPrCode),
                Norm(info.PbsPartR),
                Norm(info.PbsPartS),
                Norm(info.LevelCode),
                info.TypeId.ToString(),
                Norm(info.TypeName),
                Norm(info.Category),
                Norm(info.FamilyName));

        private static void Check(List<string> reasons, string field,
                                   string stored, string current)
        {
            if (!string.Equals(Norm(stored), Norm(current), StringComparison.Ordinal))
                reasons.Add($"{field}: '{stored}' → '{current}'");
        }

        private static string Norm(string v)
            => (v ?? string.Empty).Trim().ToUpperInvariant();

        private static string GetTypeName(Element element, Document doc)
        {
            var typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId)
                return string.Empty;
            var type = doc.GetElement(typeId);
            return type?.Name ?? string.Empty;
        }
    }
}
