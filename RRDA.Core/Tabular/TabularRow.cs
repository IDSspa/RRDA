namespace RRDA.Core.Tabular
{
    public sealed class TabularRow
    {
        public required string EntityKey { get; set; }
        public required IDictionary<string, object?> Values { get; set; }
    }
}
