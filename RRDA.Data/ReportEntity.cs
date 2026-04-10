namespace RRDA.Data
{
    public class ReportEntity
    {
        public int Id { get; set; }
        public int ReportFileId { get; set; }
        public required ReportFile ReportFile { get; set; }
        public required string EntityKind { get; set; } // es: "Module", "Board", "TestMeasurement"
        public required string Key { get; set; }        // identificatore (es: serial)
        public required ICollection<ReportProperty> Properties { get; set; }
    }
}
