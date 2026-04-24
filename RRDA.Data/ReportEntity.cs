namespace RRDA.Data
{
    public class ReportEntity
    {
        public int Id { get; set; }
        public int ReportFileId { get; set; }
        public required ReportFile ReportFile { get; set; }
        public required string ReportSheet { get; set; } // ex: "Module", "Board", "TestMeasurement" (prima EntityKind)
        public required string Key { get; set; }        // identificatore (es: serial)
        public required ICollection<ReportProperty> Properties { get; set; }
    }
}
