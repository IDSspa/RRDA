namespace RRDA.Web.Areas.Data.Controllers
{
    public class TypePivotViewModel
    {
        public int ReportTypeId { get; set; }
        public string ReportTypeKey { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = [];
        public List<TypePivotRow> Rows { get; set; } = [];
        public List<string> DynamicFilterFields { get; set; } = [];
        public int? BatchId { get; set; }
        public DateTime? UploadedFrom { get; set; }
        public DateTime? UploadedTo { get; set; }
        public string? FilterField { get; set; }
        public string? FilterFrom { get; set; }
        public string? FilterTo { get; set; }
        public string? SubjectKeyFrom { get; set; }
        public string? SubjectKeyTo { get; set; }
        public List<TypePivotBatchOption> BatchOptions { get; set; } = [];
        public int TotalFiles { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int DecimalPlaces { get; set; }
        public bool HasSubjectKey { get; set; }
        public string SubjectKeyLabel { get; set; } = string.Empty;
        public Dictionary<string, string?> HeaderUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TypePivotColumnStatistics> ColumnStatistics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
