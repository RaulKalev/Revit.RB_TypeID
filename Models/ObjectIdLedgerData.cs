using System.Collections.Generic;

namespace RB_TypeName.Models
{
    /// <summary>
    /// Root container for the RBR Object ID ledger stored in Revit Extensible Storage.
    /// </summary>
    public class ObjectIdLedgerData
    {
        public int Version { get; set; } = 1;

        /// <summary>All current ownership records, keyed by ElementUniqueId at write time.</summary>
        public List<ObjectIdLedgerRecord> Records { get; set; } = new List<ObjectIdLedgerRecord>();

        /// <summary>Object IDs that have been retired and must never be reused.</summary>
        public List<string> RetiredObjectIds { get; set; } = new List<string>();
    }
}
