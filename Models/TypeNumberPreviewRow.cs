using Autodesk.Revit.DB;
using System.ComponentModel;

namespace RB_TypeName.Models
{
    /// <summary>
    /// DataGrid row model for the Type Numbers preview tab.
    /// Implements INotifyPropertyChanged so that editable cells (L1Code, IsSelected)
    /// update the model immediately through two-way WPF bindings.
    /// </summary>
    public class TypeNumberPreviewRow : INotifyPropertyChanged
    {
        // ── Identity ─────────────────────────────────────────────────────────

        public ElementId ElementTypeId      { get; set; }
        public long      ElementTypeIdValue => ElementTypeId?.Value ?? -1;

        // ── Display fields ────────────────────────────────────────────────────

        public string DisciplineCode { get; set; } = string.Empty;
        public string Category       { get; set; } = string.Empty;
        public string FamilyName     { get; set; } = string.Empty;
        public string TypeName       { get; set; } = string.Empty;
        public int    InstanceCount  { get; set; }

        // ── PBS lookup result ─────────────────────────────────────────────────

        public string RbrPrCode       { get; set; } = string.Empty;
        public string PbsMatchStatus  { get; set; } = string.Empty;
        public string PbsTypeTemplate { get; set; } = string.Empty;

        // ── User-editable L1 code ─────────────────────────────────────────────

        private string _l1Code = string.Empty;
        public string L1Code
        {
            get => _l1Code;
            set
            {
                if (_l1Code != value)
                {
                    _l1Code = value ?? string.Empty;
                    OnPropertyChanged(nameof(L1Code));
                }
            }
        }

        // ── Type Number data ──────────────────────────────────────────────────

        public string ExistingTypeNumber { get; set; } = string.Empty;
        public string ProposedTypeNumber { get; set; } = string.Empty;
        public string Status             { get; set; } = string.Empty;
        public string Message            { get; set; } = string.Empty;

        /// <summary>Stable key for mapping storage: Category|FamilyName|TypeName (uppercase).</summary>
        public string MatchKey =>
            Category.Trim().ToUpperInvariant() + "|"
            + FamilyName.Trim().ToUpperInvariant() + "|"
            + TypeName.Trim().ToUpperInvariant();

        // ── Selection ─────────────────────────────────────────────────────────

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public bool IsReady => Status == "Ready";

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
