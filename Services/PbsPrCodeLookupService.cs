using RB_TypeName.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RB_TypeName.Services
{
    /// <summary>
    /// In-memory lookup from normalised RBR_Pr_Code → PBS row.
    /// Handles duplicate/ambiguous rows as specified in the plan.
    /// </summary>
    public class PbsPrCodeLookupService
    {
        private readonly Dictionary<string, List<PbsPrCodeRow>> _index;

        public PbsPrCodeLookupService(IEnumerable<PbsPrCodeRow> rows)
        {
            _index = new Dictionary<string, List<PbsPrCodeRow>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (!_index.TryGetValue(row.PrCodeNormalized, out var list))
                    _index[row.PrCodeNormalized] = list = new List<PbsPrCodeRow>();
                list.Add(row);
            }
        }

        /// <summary>
        /// Returns the PBS row that matches the given PrCode value.
        /// Normalises the input before comparison.
        /// </summary>
        public PbsPrCodeLookupResult FindByPrCode(string prCode)
        {
            var normalized = NormalizeCode(prCode);
            if (string.IsNullOrEmpty(normalized))
                return PbsPrCodeLookupResult.Fail("Missing RBR_Pr_Code");

            if (!_index.TryGetValue(normalized, out var rows) || rows.Count == 0)
                return PbsPrCodeLookupResult.Fail("No matching PBS row");

            if (rows.Count > 1)
            {
                bool allSame = rows.All(r =>
                    r.ObjectIdPart1 == rows[0].ObjectIdPart1 &&
                    r.ObjectIdPart2 == rows[0].ObjectIdPart2);

                if (!allSame)
                    return PbsPrCodeLookupResult.Fail(
                        "Ambiguous PBS PrCode mapping",
                        "Multiple PBS rows found with different Object ID codes for this PrCode.");

                // Duplicate rows but same R/S — usable with a warning.
                return PbsPrCodeLookupResult.Ok(
                    rows[0],
                    "Duplicate PBS PrCode rows with same Object ID codes");
            }

            return PbsPrCodeLookupResult.Ok(rows[0]);
        }

        private static string NormalizeCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().Replace("\u00A0", " ").ToUpperInvariant();
        }
    }
}
