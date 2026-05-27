using System.Collections.Generic;

namespace RB_TypeName.Models
{
    /// <summary>
    /// Counts returned by <see cref="Services.TypeNumberMappingExtensibleStorageService.Upsert"/>.
    /// </summary>
    public class TypeNumberMappingUpsertResult
    {
        public int Added           { get; set; }
        public int Updated         { get; set; }
        public int SkippedExisting { get; set; }
        public int Invalid         { get; set; }

        public List<string> Issues { get; set; } = new List<string>();

        /// <summary>Total records written (Added + Updated).</summary>
        public int TotalChanged => Added + Updated;
    }
}
