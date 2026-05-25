namespace RRDA.Core.Tabular
{
    public sealed class TabularRequest
    {
        public int ReportTypeId { get; set; }
        public int? ReportBatchId { get; set; }
        public IReadOnlyCollection<int>? ReportFileIds { get; set; }
        public string? ReportSheet { get; set; }
        public DateTime? UploadedFromUtc { get; set; }
        public DateTime? UploadedToUtc { get; set; }
        public int? MaxRows { get; set; }
        public string? UserId { get; set; }
    }

    public sealed class TabularSchema
    {
        public required string ReportTypeKey { get; set; }
        public required IReadOnlyCollection<TabularColumnDefinition> Columns { get; set; }
    }

    public sealed class TabularColumnDefinition
    {
        public required string SourcePropertyName { get; set; }
        public required string ColumnName { get; set; }
        public required string DataType { get; set; }
        public string? Unit { get; set; }
        public bool IsMeasure { get; set; }
        public string AggregationHint { get; set; } = "none";
    }

    public sealed class TabularResult
    {
        public required TabularSchema Schema { get; set; }
        public required IReadOnlyCollection<TabularRow> Rows { get; set; }
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class TabularRow
    {
        public required string EntityKey { get; set; }
        public required IDictionary<string, object?> Values { get; set; }
    }
}
