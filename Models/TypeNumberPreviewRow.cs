using Autodesk.Revit.DB;
using System.ComponentModel;

namespace RB_TypeName.Models
{
    /// <summary>
    /// DataGrid row model for the Type Numbers preview tab.
    /// Implements INotifyPropertyChanged for editable cells (L1Code, IsSelected)
    /// and for MappingStatus (updated when L1Code changes via user input).
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

        // ── Type source identity ──────────────────────────────────────────────

        /// <summary>Parameter name used to identify this type (e.g. "Revit Type Name").</summary>
        public string TypeSourceParameterName { get; set; } = string.Empty;

        /// <summary>Value of the type-source parameter for this row.</summary>
        public string TypeSourceValue         { get; set; } = string.Empty;

        // ── PBS lookup result ─────────────────────────────────────────────────

        public string RbrPrCode       { get; set; } = string.Empty;
        public string PbsMatchStatus  { get; set; } = string.Empty;
        public string PbsTypeTemplate { get; set; } = string.Empty;

        // ── User-editable L1 code ─────────────────────────────────────────────

        private string _l1Code = string.Empty;
        private bool   _suppressUserEditFlag;

        public string L1Code
        {
            get => _l1Code;
            set
            {
                if (_l1Code != value)
                {
                    _l1Code = value ?? string.Empty;
                    if (!_suppressUserEditFlag)
                    {
                        HasSavedMapping      = false;
                        HasUserEditedMapping = true;
                        MappingStatus        = "Edited";
                    }
                    OnPropertyChanged(nameof(L1Code));
                    OnPropertyChanged(nameof(MappingStatus));
                }
            }
        }

        // ── Mapping state ─────────────────────────────────────────────────────

        /// <summary>True when L1Code came from Extensible Storage (saved/imported).</summary>
        public bool   HasSavedMapping      { get; set; }

        /// <summary>True when the user manually edited L1Code in the grid.</summary>
        public bool   HasUserEditedMapping { get; set; }

        /// <summary>Human-readable mapping origin: Saved | Imported | Edited | PBS suggestion | Unmapped.</summary>
        public string MappingStatus        { get; set; } = "Unmapped";

        // ── Type Number data ──────────────────────────────────────────────────

        public string ExistingTypeNumber { get; set; } = string.Empty;
        public string ProposedTypeNumber { get; set; } = string.Empty;
        public string Status             { get; set; } = string.Empty;
        public string Message            { get; set; } = string.Empty;

        // ── Match key (primary key for mapping lookups) ───────────────────────

        /// <summary>
        /// Normalised key used to match this row against <see cref="TypeNumberMappingRecord"/> entries.
        /// Format: DisciplineCode|RbrPrCode|TypeSourceParameterName|TypeSourceValue (uppercase, trimmed).
        /// </summary>
        public string MatchKey =>
            Norm(DisciplineCode)          + "|"
            + Norm(RbrPrCode)             + "|"
            + Norm(TypeSourceParameterName) + "|"
            + Norm(TypeSourceValue);

        private static string Norm(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();

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

        // ── Programmatic L1Code helpers ───────────────────────────────────────

        /// <summary>
        /// Sets L1Code from a saved/imported mapping without triggering the user-edit flag.
        /// Updates <see cref="HasSavedMapping"/> and <see cref="MappingStatus"/> explicitly.
        /// </summary>
        public void ApplyL1CodeFromMapping(string l1Code, string mappingStatus = "Saved",
                                           bool hasSavedMapping = true)
        {
            _suppressUserEditFlag = true;
            L1Code                = l1Code ?? string.Empty;
            _suppressUserEditFlag = false;
            HasSavedMapping       = hasSavedMapping;
            HasUserEditedMapping  = false;
            MappingStatus         = mappingStatus;
            OnPropertyChanged(nameof(MappingStatus));
        }

        /// <summary>
        /// Sets L1Code from a PBS template suggestion without triggering the user-edit flag.
        /// Only updates <see cref="MappingStatus"/> if no saved or user-edited mapping exists.
        /// </summary>
        public void ApplyL1CodeFromPbs(string l1Code)
        {
            _suppressUserEditFlag = true;
            L1Code                = l1Code ?? string.Empty;
            _suppressUserEditFlag = false;
            if (!HasSavedMapping && !HasUserEditedMapping)
                MappingStatus = "PBS suggestion";
            OnPropertyChanged(nameof(MappingStatus));
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

