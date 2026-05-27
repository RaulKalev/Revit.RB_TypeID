using Autodesk.Revit.DB;

namespace RB_TypeName.Models
{
    /// <summary>
    /// A single row in the preview table shown before writing RBR-Object_IDs.
    /// Also used for apply-phase status tracking.
    /// </summary>
    public class ObjectIdPreviewRow
    {
        public bool IsSelected { get; set; } = true;

        public ElementId ElementId { get; set; }
        public string Category     { get; set; } = string.Empty;
        public string FamilyName   { get; set; } = string.Empty;
        public string TypeName     { get; set; } = string.Empty;

        public string LevelName { get; set; } = string.Empty;
        public string LevelCode { get; set; } = string.Empty;

        public string ExistingObjectId { get; set; } = string.Empty;
        public string RbrPrCode        { get; set; } = string.Empty;

        public string PbsPartR { get; set; } = string.Empty;
        public string PbsPartS { get; set; } = string.Empty;

        public string ProposedObjectId { get; set; } = string.Empty;

        public string Status  { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        // Flat value for DataGrid display — .Value works in both net48 and net8.0-windows.
        public long ElementIdValue => ElementId?.Value ?? -1;

        public bool IsReady => Status == "Ready";
    }
}
