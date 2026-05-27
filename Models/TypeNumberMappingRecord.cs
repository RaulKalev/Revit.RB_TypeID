namespace RB_TypeName.Models
{
    /// <summary>
    /// Persisted mapping record.
    /// Primary key: DisciplineCode|RbrPrCode|TypeSourceParameterName|TypeSourceValue.
    /// Display context fields (Category, FamilyName, RevitTypeName) are stored for
    /// diagnostics and export context but are NOT part of the match key.
    /// </summary>
    public class TypeNumberMappingRecord
    {
        // ── Primary key fields ────────────────────────────────────────────────
        public string DisciplineCode          { get; set; } = string.Empty;
        public string RbrPrCode               { get; set; } = string.Empty;
        public string TypeSourceParameterName { get; set; } = string.Empty;
        public string TypeSourceValue         { get; set; } = string.Empty;

        // ── Mapping value ─────────────────────────────────────────────────────
        public string L1Code { get; set; } = string.Empty;
        public string Notes  { get; set; } = string.Empty;

        // ── Display context (not used as key) ─────────────────────────────────
        public string Category      { get; set; } = string.Empty;
        public string FamilyName    { get; set; } = string.Empty;
        public string RevitTypeName { get; set; } = string.Empty;
        public long   LastSeenElementTypeId { get; set; }

        // ── Timestamps ────────────────────────────────────────────────────────
        public string CreatedUtc { get; set; } = string.Empty;
        public string UpdatedUtc { get; set; } = string.Empty;

        // ── Computed ──────────────────────────────────────────────────────────
        public string MatchKey =>
            Norm(DisciplineCode)          + "|"
            + Norm(RbrPrCode)             + "|"
            + Norm(TypeSourceParameterName) + "|"
            + Norm(TypeSourceValue);

        private static string Norm(string s) =>
            (s ?? string.Empty).Trim().ToUpperInvariant();
    }
}
