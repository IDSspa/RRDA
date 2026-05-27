namespace RRDA.Data
{
    /// <summary>
    /// Proprietà normalizzata di una ReportEntity (mappato su tabella ReportProperties).
    /// </summary>
    public class ReportProperty
    {
        public int Id { get; set; }

        public int ReportEntityId { get; set; }

        public required ReportEntity ReportEntity { get; set; }

        // Nome della proprietà (es: "Voltage", "FirmwareVersion")
        public required string Name { get; set; }

        // Valore normalizzato in stringa
        public string? Value { get; set; }

        // Unità opzionale (es: "V", "°C")
        public string? Unit { get; set; }

        // Tipo dei dati rappresentati (es: "int","double","datetime","string")
        public required string DataType { get; set; }

        // Indica se questa proprietà è la chiave soggetto (es: serial number)
        public required bool IsSubjectKey { get; set; } = false;
    }
}
