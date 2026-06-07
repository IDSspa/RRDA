namespace RRDA.Data
{
    /// <summary>
    /// Eccezione sollevata da <see cref="IImportResultRepository.SaveAsync"/> quando
    /// la strategia scelta è <see cref="DuplicateImportStrategy.Block"/> e il file
    /// risulta già presente in DB.
    /// Consente al chiamante di distinguere un blocco intenzionale da un errore DB.
    /// </summary>
    public sealed class DuplicateImportException : InvalidOperationException
    {
        public string FileName { get; }
        public int ExistingCount { get; }

        public DuplicateImportException(string fileName, int existingCount)
            : base($"Import bloccato: '{fileName}' è già presente nel database ({existingCount} volta/e).")
        {
            FileName = fileName;
            ExistingCount = existingCount;
        }
    }
}
