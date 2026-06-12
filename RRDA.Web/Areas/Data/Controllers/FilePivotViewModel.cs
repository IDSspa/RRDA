namespace RRDA.Web.Areas.Data.Controllers
{
    public class FilePivotViewModel
    {
        public int FileId { get; set; }
        public int ReportTypeId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ReportTypeKey { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = [];
        public List<TypePivotRangeDescriptor> RangeHeaders { get; set; } = [];
        public Dictionary<string, string?> HeaderUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ReferenceHeaders { get; set; } = [];
        public bool HasSubjectKey { get; set; }
        public string SubjectKeyLabel { get; set; } = "SubjectKey";
        public TypePivotRow Row { get; set; } = new();
        public int DecimalPlaces { get; set; }
    }
}
