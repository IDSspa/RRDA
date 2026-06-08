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
        public string? RangeName { get; set; }
        public string? RowIndex { get; set; }
        public string? ColIndex { get; set; }
        public bool IsRange => IsRangeIndex(RowIndex) || IsRangeIndex(ColIndex);

        private static bool IsRangeIndex(string? value) => int.TryParse(value, out var index) && index >= 0;
    }
}
