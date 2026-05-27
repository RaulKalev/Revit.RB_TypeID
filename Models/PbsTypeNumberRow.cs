namespace RB_TypeName.Models
{
    /// <summary>
    /// A row from the PBS Excel sheet that carries RBR-Type_number template data.
    /// Used to build a per-PrCode lookup for the Type Numbers workflow.
    /// </summary>
    public class PbsTypeNumberRow
    {
        public int    RowNumber          { get; set; }
        public string PrCodeRaw          { get; set; } = string.Empty;
        public string PrCodeNormalized   { get; set; } = string.Empty;
        public string DisciplineCode     { get; set; } = string.Empty;  // Column R
        public string ObjectCode         { get; set; } = string.Empty;  // Column S

        /// <summary>
        /// Optional type number template from the PBS sheet, e.g. "CAM-01ZZZZ".
        /// The literal text "ZZZZ" is replaced with the running 4-digit number.
        /// Empty when no template column is present or the cell is blank.
        /// </summary>
        public string TypeNumberTemplate { get; set; } = string.Empty;
    }
}
