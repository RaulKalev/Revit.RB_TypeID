using System.Collections.Generic;

namespace RB_TypeName.Models
{
    /// <summary>Summary result of a reconcile preview operation.</summary>
    public class ObjectIdReconcileResult
    {
        public List<ObjectIdReconcileRow> Rows { get; set; } = new List<ObjectIdReconcileRow>();

        public int ValidCount     { get; set; }
        public int MissingCount   { get; set; }
        public int DuplicateCount { get; set; }
        public int ChangedCount   { get; set; }
        public int ErrorCount     { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
