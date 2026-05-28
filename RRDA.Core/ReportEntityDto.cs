namespace RRDA.Core
{
    public class ReportEntityDto
    {
        public required string EntityKind { get; set; }
        public required string Key { get; set; }
        public Dictionary<string, string> Properties { get; set; } = [];
        public Dictionary<string, string> PropertyDataTypes { get; set; } = [];
    }
}
