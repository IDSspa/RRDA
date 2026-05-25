namespace RRDA.Data
{
    /// <summary>
    /// Cache sessionale della proiezione tabellare per analisi web.
    /// </summary>
    public class TabularSession
    {
        public Guid Id { get; set; }
        public int ReportTypeId { get; set; }
        public required ReportType ReportType { get; set; }
        public string? UserId { get; set; }
        public required string FilterHash { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime SourceWatermark { get; set; }
        public required ICollection<TabularSessionRow> Rows { get; set; }
    }
}
