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
}
