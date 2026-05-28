using System.Collections.Generic;

namespace RB_TypeName.Models
{
    /// <summary>
    /// A single ownership record stored in the Object ID ledger.
    /// Tracks which element was originally assigned a given RBR-Object_ID.
    /// </summary>
    public class ObjectIdLedgerRecord
    {
        public string ObjectId          { get; set; } = string.Empty;
        public string ElementUniqueId   { get; set; } = string.Empty;
        public long   ElementId         { get; set; }

        public string RbrPrCode  { get; set; } = string.Empty;
        public string PbsPartR   { get; set; } = string.Empty;
        public string PbsPartS   { get; set; } = string.Empty;
        public string LevelCode  { get; set; } = string.Empty;
        public string Prefix     { get; set; } = string.Empty;

        public string Category   { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public long   TypeId     { get; set; }
        public string TypeName   { get; set; } = string.Empty;

        public string Fingerprint { get; set; } = string.Empty;

        public string AssignedUtc { get; set; } = string.Empty;
        public string UpdatedUtc  { get; set; } = string.Empty;

        /// <summary>Object IDs previously held by this element (retained for history).</summary>
        public List<string> PreviousObjectIds { get; set; } = new List<string>();
    }
}
