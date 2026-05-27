using Autodesk.Revit.DB;

namespace RB_TypeName.Models
{
    public class RbrIdAssignmentResult
    {
        public ElementId ElementId { get; set; }
        public string Status { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Message { get; set; }

        // Flat value for DataGrid display — uses .Value (long) which works in both
        // Revit 2024 (net48) and Revit 2026 (net8.0-windows) where IntegerValue was removed.
        public long ElementIdValue => ElementId?.Value ?? -1;
    }
}
