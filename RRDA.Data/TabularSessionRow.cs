namespace RRDA.Data
{
    public class TabularSessionRow
    {
        public long Id { get; set; }
        public Guid TabularSessionId { get; set; }
        public required TabularSession TabularSession { get; set; }
        public int RowIndex { get; set; }
        public required string EntityKey { get; set; }
        public required string JsonData { get; set; }
    }
}
