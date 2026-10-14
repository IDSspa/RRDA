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
        public string? VisibleInPivotValue { get; set; }
        public string? FilterableValue { get; set; }
        public bool IsRange => IsRangeIndex(RowIndex) || IsRangeIndex(ColIndex);
        public bool? VisibleInPivot => VisibleInPivotValue switch
        {
            "1" => true,
            "0" => false,
            _ => null
        };
        public bool? Filterable => FilterableValue switch { "1" => true, "0" => false, _ => null };

        private static bool IsRangeIndex(string? value) => int.TryParse(value, out var index) && index >= 0;
    }
}
