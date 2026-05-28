using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace RB_TypeName.Models
{
    /// <summary>
    /// A single row in the Object ID reconcile preview grid.
    /// Describes the current state of an element and what (if anything) should change.
    /// </summary>
    public class ObjectIdReconcileRow
    {
        public bool IsSelected { get; set; } = true;

        public ElementId ElementId      { get; set; }
        public long      ElementIdValue => ElementId?.Value ?? -1;
        public string    UniqueId       { get; set; } = string.Empty;

        public string Category   { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName   { get; set; } = string.Empty;
        public long   TypeIdValue { get; set; }

        public string RbrPrCode  { get; set; } = string.Empty;
        public string LevelCode  { get; set; } = string.Empty;

        public string CurrentObjectId  { get; set; } = string.Empty;
        public string ProposedObjectId { get; set; } = string.Empty;

        public string CurrentPrefix  { get; set; } = string.Empty;
        public string ExpectedPrefix { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;

        public List<string> ChangeReasons { get; set; } = new List<string>();

        /// <summary>Semicolon-separated change reasons for DataGrid display.</summary>
        public string ChangeReasonsText
            => ChangeReasons.Count > 0 ? string.Join("; ", ChangeReasons) : string.Empty;

        public string PreviousTypeName  { get; set; } = string.Empty;
        public string PreviousRbrPrCode { get; set; } = string.Empty;
        public string PreviousLevelCode { get; set; } = string.Empty;

        public string Fingerprint       { get; set; } = string.Empty;
        public string StoredFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// True when the reconcile service determined this row may be written.
        /// The apply handler still re-validates before writing.
        /// </summary>
        public bool IsWriteAllowed { get; set; }
    }
}
