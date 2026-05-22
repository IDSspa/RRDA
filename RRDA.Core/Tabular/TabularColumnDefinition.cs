namespace RRDA.Core.Tabular
{
    public sealed class TabularColumnDefinition
    {
        public required string SourcePropertyName { get; set; }
        public required string ColumnName { get; set; }
        public required string DataType { get; set; }
        public string? Unit { get; set; }
        public bool IsMeasure { get; set; }
        public string AggregationHint { get; set; } = "none";
    }
}
