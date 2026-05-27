namespace RB_TypeName.Models
{
    public class ParsedRbrObjectId
    {
        public string DisciplineCode { get; set; }
        public string ObjectCode { get; set; }
        public string LevelCode { get; set; }
        public int RunningNumber { get; set; }

        public string Prefix => $"{DisciplineCode}-{ObjectCode}-{LevelCode}";
    }
}
