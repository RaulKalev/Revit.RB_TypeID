namespace RB_TypeName.Models
{
    /// <summary>Controls which elements are included and what repairs are allowed during reconcile.</summary>
    public class ObjectIdReconcileOptions
    {
        /// <summary>Include elements that have no Object ID and generate new ones.</summary>
        public bool IncludeMissingIds { get; set; } = true;

        /// <summary>Allow repairing copied/duplicate elements by assigning new IDs.</summary>
        public bool RepairDuplicates { get; set; } = true;

        /// <summary>Detect elements whose type/level/PBS classification has changed.</summary>
        public bool FlagChangedElements { get; set; } = true;

        /// <summary>
        /// When true, elements with a changed fingerprint may have their Object ID reassigned.
        /// Has no effect unless FlagChangedElements is also true.
        /// </summary>
        public bool ReassignChangedElements { get; set; } = false;

        /// <summary>Include rows with Status == "Valid" in the result for review.</summary>
        public bool IncludeValidRows { get; set; } = true;
    }
}
