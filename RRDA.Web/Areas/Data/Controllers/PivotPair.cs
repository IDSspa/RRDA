namespace RRDA.Web.Areas.Data.Controllers
{
    public class PivotPair
    {
        public int FileId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
        public bool IsSubjectKey { get; set; }
        public string DataType { get; set; } = string.Empty;
        public string? Unit { get; set; }
    }
}
