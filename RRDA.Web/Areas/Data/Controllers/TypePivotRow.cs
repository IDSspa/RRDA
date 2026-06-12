namespace RRDA.Web.Areas.Data.Controllers
{
    public class TypePivotRow
    {
        public int FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public int? BatchId { get; set; }
        public string? BatchName { get; set; }
        public string? SubjectKey { get; set; }
        public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ReferenceTargetFileIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TypePivotRangeCell> Ranges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
