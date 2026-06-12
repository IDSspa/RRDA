namespace RRDA.Core.Validator
{
    public class FieldRule
    {
        public string DefinedName { get; set; } = "";
        public bool Required { get; set; } = false;
        public FieldDataType DataType { get; set; } = FieldDataType.String;
        public string? Pattern { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public int? MaxLength { get; set; }
        public string? Format { get; set; } // es: yyyy-MM-dd
        public string? Unit { get; set; }
        public Range? Range { get; set; }
        public string? ReferenceReportType { get; set; }
        public string? ReferenceKeyField { get; set; }
    }
}
