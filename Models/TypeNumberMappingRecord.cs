namespace RB_TypeName.Models
{
    /// <summary>
    /// Persisted mapping record that links a Revit ElementType (identified by the stable
    /// Category|FamilyName|TypeName key) to a stored L1 code and optional notes.
    /// Stored in a per-document JSON file in the settings folder.
    /// </summary>
    public class TypeNumberMappingRecord
    {
        public string Category      { get; set; } = string.Empty;
        public string FamilyName    { get; set; } = string.Empty;
        public string RevitTypeName { get; set; } = string.Empty;
        public string RbrPrCode     { get; set; } = string.Empty;
        public string L1Code        { get; set; } = string.Empty;
        public string Notes         { get; set; } = string.Empty;
        public string UpdatedUtc    { get; set; } = string.Empty;

        /// <summary>
        /// Stable cross-model key used for import/export matching.
        /// Normalised: uppercase, trimmed.
        /// </summary>
        public string MatchKey =>
            Category.Trim().ToUpperInvariant() + "|"
            + FamilyName.Trim().ToUpperInvariant() + "|"
            + RevitTypeName.Trim().ToUpperInvariant();
    }
}
