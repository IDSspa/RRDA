using RRDA.Core.Validator;

namespace RRDA.Core
{
    public interface IReportImporter
    {
        string Name { get; }            // es: "RadTest_v1"
        string Version { get; }
        string SupportedFileExtension { get; } // ".xlsx"
        string MatchingPattern { get; }
        ReportSubjectKind SubjectKind { get; }
        /// <summary>
        /// Verifica se il plugin è idoneo al caricamento del file di report fornito.
        /// Deve essere usato prima di chiamare <see cref="ImportAsync"/>.
        /// </summary>
        /// <param name="fileName">Nome del file di report da verificare.</param>
        /// <param name="validationConfigXml">(Opzionale) Stream del file XML di configurazione di validazione associato al plugin.</param>
        /// 
        /// <returns>True se il plugin può gestire il file; altrimenti false.</returns>
        Task<bool> CanImportAsync(string fileName, Stream? validationConfigXml = null);
        Task<ImportResult> ImportAsync(
                Stream fileStream,
                ValidationConfig config,
                IProgress<ImportProgress>? progress = null,         // <-- opzionale
                CancellationToken ct = default
        );
        // validationConfigXml = XML file stream associato al plugin
    }
}
