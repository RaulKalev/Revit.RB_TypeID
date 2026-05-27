using System.Collections.Generic;

namespace RB_TypeName.Models
{
    public class TypeNumberMappingImportResult
    {
        public int          Imported  { get; set; }
        public int          Updated   { get; set; }
        public int          Skipped   { get; set; }
        public int          Invalid   { get; set; }
        public int          Conflicts { get; set; }
        public List<string> Issues    { get; } = new List<string>();
    }
}
