namespace RRDA.Core
{
    public class ImportResult
    {
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = [];
        public required string ReportTypeKey { get; set; } // chiave che identifica il ReportType
        public IEnumerable<ReportEntityDto>? Entities { get; set; }
    }
}
