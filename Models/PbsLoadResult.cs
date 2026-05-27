using System.Collections.Generic;

namespace RB_TypeName.Models
{
    /// <summary>
    /// Wraps the result of a PBS Excel load attempt so the caller
    /// always gets structured feedback instead of exceptions.
    /// </summary>
    public class PbsLoadResult
    {
        public bool             Success      { get; private set; }
        public string           ErrorMessage { get; private set; }
        public List<PbsMapping> Mappings     { get; private set; } = new List<PbsMapping>();

        public static PbsLoadResult Ok(List<PbsMapping> mappings)
            => new PbsLoadResult { Success = true, Mappings = mappings };

        public static PbsLoadResult Fail(string message)
            => new PbsLoadResult { Success = false, ErrorMessage = message };
    }
}
