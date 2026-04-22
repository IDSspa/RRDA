namespace RRDA.Core
{
    /// <summary>
    /// Dati di avanzamento riportati dal plugin durante ImportAsync.
    /// </summary>
    public sealed class ImportProgress(int processedItems, int totalItems, string? message = null)
    {
        /// <summary>Numero di campi/righe già elaborati nel file corrente.</summary>
        public int ProcessedItems { get; } = processedItems;
        /// <summary>Totale campi/righe del file corrente (0 = sconosciuto).</summary>
        public int TotalItems { get; } = totalItems;
        /// <summary>Messaggio descrittivo opzionale.</summary>
        public string? Message { get; } = message;

        /// <summary>
        /// Percentuale 0-100 calcolata sul file corrente;
        /// restituisce -1 se TotalItems non è noto.
        /// </summary>
        public int PercentFile =>
            TotalItems > 0 ? (int)Math.Clamp(ProcessedItems * 100.0 / TotalItems, 0, 100) : -1;
    }
}
