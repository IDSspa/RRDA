namespace RRDA.Core.Tabular
{
    public sealed class TabularSchema
    {
        public required string ReportTypeKey { get; set; }
        public required IReadOnlyCollection<TabularColumnDefinition> Columns { get; set; }
    }
}
