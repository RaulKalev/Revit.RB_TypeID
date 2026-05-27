namespace RB_TypeName.Models
{
    /// <summary>
    /// Result of a PrCode → PBS row lookup. Carries success/failure, an optional
    /// warning (e.g. duplicate rows with identical R/S), and the matched row.
    /// </summary>
    public class PbsPrCodeLookupResult
    {
        public bool         Success { get; private set; }
        public string       Status  { get; private set; }
        public string       Warning { get; private set; }
        public string       Message { get; private set; }
        public PbsPrCodeRow Row     { get; private set; }

        public static PbsPrCodeLookupResult Ok(PbsPrCodeRow row, string warning = null)
            => new PbsPrCodeLookupResult { Success = true, Row = row, Warning = warning };

        public static PbsPrCodeLookupResult Fail(string status, string message = null)
            => new PbsPrCodeLookupResult { Success = false, Status = status, Message = message };
    }
}
