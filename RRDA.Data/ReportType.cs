using RRDA.Core;

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

        // Soggetto di riferimento del report (componente, sottoassieme, radar)
        public ReportSubjectKind SubjectKind { get; set; } = ReportSubjectKind.Component;

        // Navigazione verso i file associati
        public required ICollection<ReportFile> Files { get; set; } = [];

        // Navigazione verso le sessioni cache tabellare
        public required ICollection<TabularSession> TabularSessions { get; set; } = [];
    }
}
