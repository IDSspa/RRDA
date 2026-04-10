namespace RRDA.Data
{
    /// <summary>
    /// Rappresenta un file importato (mappato su tabella ReportFiles).
    /// </summary>
    public class ReportFile
    {
        public int Id { get; set; }

        public required string FileName { get; set; }

        // Percorso completo del file (incluso il nome del file)
        public required string FullPath { get; set; }

        public DateTime UploadedAt { get; set; }

        public int ReportTypeId { get; set; }

        public required ReportType ReportType { get; set; }

        // Utente che ha importato (opzionale)
        public string? ImportedBy { get; set; }

        // Entità contenute nel file
        public required ICollection<ReportEntity> Entities { get; set; } = new List<ReportEntity>();
    }
}
