namespace RB_TypeName.Models
{
    public class PbsMapping
    {
        public int    SourceRowNumber       { get; set; }           // Row in the PBS worksheet
        public string DisciplineCode        { get; set; }           // Column R
        public string ObjectCode            { get; set; }           // Column S
        public string SecondaryObjectCode   { get; set; }           // Column T — loaded but not used in ID yet
        public string Description           { get; set; }           // Column K — display name / description

        /// <summary>The R-S code pair used as the ID prefix base (e.g. "ME-DUC").</summary>
        public string PbsCode => $"{DisciplineCode}-{ObjectCode}";

        /// <summary>Kept for backward compatibility — same value as PbsCode.</summary>
        public string Prefix => PbsCode;

        /// <summary>Display text shown in ComboBoxes: "ME-DUC — Ventilation ducts".</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(Description)
            ? PbsCode
            : $"{PbsCode}  —  {Description}";

        /// <summary>Kept for backward compatibility — same value as DisplayName.</summary>
        public string DisplayText => DisplayName;

        public override string ToString() => DisplayName;
    }
}
