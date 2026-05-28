namespace RRDA.Web.Areas.Data.Controllers
{
    public class FilePivotViewModel
    {
        public int FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ReportTypeKey { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = [];
        public Dictionary<string, string?> Row { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
