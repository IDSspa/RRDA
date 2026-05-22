namespace RRDA.Core.Tabular
{
    public sealed class TabularResult
    {
        public required TabularSchema Schema { get; set; }
        public required IReadOnlyCollection<TabularRow> Rows { get; set; }
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
