namespace RRDA.Data
{
    /// <summary>
    /// Tipo di report (mappato su tabella ReportTypes).
    /// </summary>
    public class ReportType
    {
        public int Id { get; set; }

        // Chiave utilizzata nelle lookup (colonna [Key] in DB)
        public required string Key { get; set; }

        // Nome leggibile del tipo (es: "FunctionalTest")
        public required string Name { get; set; }

        public string Description { get; set; } = string.Empty;

        // Navigazione verso i file associati
        public required ICollection<ReportFile> Files { get; set; } = [];

        // Navigazione verso le sessioni cache tabellare
        public required ICollection<TabularSession> TabularSessions { get; set; } = [];
    }
}
