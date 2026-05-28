namespace RB_TypeName.Models
{
    /// <summary>
    /// Represents the current identity state of an element, used to detect
    /// whether it has changed since its Object ID was last assigned.
    /// </summary>
    public class ObjectFingerprintInfo
    {
        public string RbrPrCode  { get; set; } = string.Empty;
        public string PbsPartR   { get; set; } = string.Empty;
        public string PbsPartS   { get; set; } = string.Empty;
        public string LevelCode  { get; set; } = string.Empty;
        public string Prefix     { get; set; } = string.Empty;
        public long   TypeId     { get; set; }
        public string TypeName   { get; set; } = string.Empty;
        public string Category   { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;

        /// <summary>
        /// Normalized pipe-separated string of the identity fields.
        /// Format: RbrPrCode|PbsPartR|PbsPartS|LevelCode|TypeId|TypeName|Category|FamilyName
        /// </summary>
        public string Fingerprint { get; set; } = string.Empty;
    }
}
