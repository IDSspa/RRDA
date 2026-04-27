namespace RRDA.Data
{    
     /// <summary>
     /// Strategia scelta dall'utente quando un file con lo stesso FullPath
     /// è già presente in ReportFiles.
     /// </summary>
    public enum DuplicateImportStrategy
    {
        /// <summary>
        /// Interrompe l'import senza modificare i dati esistenti.
        /// </summary>
        Block,

        /// <summary>
        /// Elimina il ReportFile esistente (e tutti i figli in cascata)
        /// e inserisce ex novo il risultato del nuovo import.
        /// Il vecchio Id non viene preservato.
        /// </summary>
        Replace,

        /// <summary>
        /// Inserisce un ulteriore ReportFile affiancato a quelli esistenti,
        /// mantenendo la storia completa degli import.
        /// </summary>
        NewVersion
    }
}
