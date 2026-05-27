namespace RB_TypeName.Models
{
    /// <summary>
    /// Represents a single row in the PBS Excel sheet used for RBR_Pr_Code lookup.
    /// </summary>
    public class PbsPrCodeRow
    {
        public int    RowNumber        { get; set; }
        public string PrCodeRaw        { get; set; }
        public string PrCodeNormalized { get; set; }
        public string ObjectIdPart1    { get; set; } // Column R — Discipline/system code
        public string ObjectIdPart2    { get; set; } // Column S — Object/type code
    }
}
