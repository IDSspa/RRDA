namespace RRDA.Data
{
    public class ReportBatch
    {
        public int Id { get; set; }

        public required string Name { get; set; }

        public string? Description { get; set; }

        // Indica se il batch rappresenta un'attività di manutenzione
        public bool IsMaintenance { get; set; }

        // File appartenenti al batch (1-N)
        public ICollection<ReportFile> ReportFiles { get; set; } = [];
    }
}
