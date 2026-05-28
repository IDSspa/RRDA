using Microsoft.EntityFrameworkCore;
using RRDA.Core;

namespace RRDA.Data
{
    /// <summary>
    /// Repository per la persistenza del risultato di import.
    /// Implementazione EF Core (usa il modello RRDADbContext).
    /// </summary>
    public static class ImportResultRepository
    {
        /// <summary>
        /// Salva ImportResult nel database usando il DbContext EF Core fornito.
        /// Restituisce una tupla con (ReportFileId, EntitiesSaved, PropertiesSaved).
        /// </summary>
        public static async Task<(int ReportFileId, int EntitiesSaved, int PropertiesSaved)> SaveAsync(string fileName,
                                                                                                       string fullPath,
                                                                                                       ImportResult importResult,
                                                                                                       RRDADbContext db,
                                                                                                       Action<string>? logger = null,
                                                                                                       string? user = null,
                                                                                                       int? batchId = null, DuplicateImportStrategy duplicateStrategy = DuplicateImportStrategy.NewVersion)
        {
            ArgumentNullException.ThrowIfNull(db);
            ArgumentNullException.ThrowIfNull(importResult);
            
            if (string.IsNullOrWhiteSpace(fileName)) 
                throw new ArgumentException("FileName non valido.", nameof(fileName));

            // transazione EF
            await using var tran = await db.Database.BeginTransactionAsync();

            try
            {
                // Recupera il ReportType tramite la Key (colonna Key)
                var reportType = await db.ReportTypes
                                         .AsTracking()
                                         .FirstOrDefaultAsync(rt => rt.Key == importResult.ReportTypeKey) ?? throw new InvalidOperationException($"ReportType con chiave '{importResult.ReportTypeKey}' non trovato in ReportTypes.");

                // -- Gestione duplicato --
                var existingFiles = await db.ReportFiles
                                            .Where(f =>
                                                f.FileName == fileName &&
                                                f.ReportTypeId == reportType.Id)
                                            .ToListAsync();

                if (existingFiles.Count > 0) {
                    switch (duplicateStrategy)
                    {
                        case DuplicateImportStrategy.Block:
                            // Interrompe senza toccare il DB; il chiamante intercetta
                            // l'eccezione come segnale di import bloccato.
                            throw new DuplicateImportException(fileName, existingFiles.Count);

                        case DuplicateImportStrategy.Replace:
                            // DELETE in cascata di tutti i ReportFile esistenti per questo
                            // FullPath + ReportType. Il cascade su ReportEntities e
                            // ReportProperties è configurato in OnModelCreating.
                            db.ReportFiles.RemoveRange(existingFiles);
                            await db.SaveChangesAsync();
                            logger?.Invoke(
                                $"[Data] Replace: eliminati {existingFiles.Count} ReportFile esistenti per '{fileName}'.");
                            break;

                        case DuplicateImportStrategy.NewVersion:
                            // Nessuna azione: procede con l'insert affiancato.
                            logger?.Invoke(
                                $"[Data] NewVersion: mantenuti {existingFiles.Count} ReportFile esistenti per '{fileName}', aggiunto nuovo.");
                            break;
                    }
                }

                var reportFile = new ReportFile
                {
                    FileName = fileName,
                    FullPath = fullPath,
                    UploadedAt = DateTime.UtcNow,
                    ReportTypeId = reportType.Id,
                    ReportType = reportType,
                    ImportedBy = user,
                    ReportBatchId = batchId,
                    Entities = []
                };

                var entitiesSaved = 0;
                var propertiesSaved = 0;

                if (importResult.Entities != null && importResult.Entities.Any())
                {
                    foreach (var dto in importResult.Entities)
                    {
                        var entity = new ReportEntity
                        {
                            ReportSheet = dto.EntityKind ?? string.Empty,
                            Key = dto.Key ?? string.Empty,
                            ReportFile = reportFile,
                            Properties = []
                        };

                        if (dto.Properties != null && dto.Properties.Count > 0)
                        {
                            var dataType = dto.Properties.TryGetValue("data_type", out var dt) && !string.IsNullOrWhiteSpace(dt)
    ? dt
    : "string";

                            var unit = dto.Properties.TryGetValue("unit", out var u) && !string.IsNullOrWhiteSpace(u)
                                ? u
                                : null;

                            foreach (var kv in dto.Properties)
                            {
                                // Legge il flag dal dizionario della stessa entità
                                bool isSubjectKey = false;
                                if (dto.Properties != null)
                                {
                                    isSubjectKey = dto.Properties.TryGetValue("is_subject_key", out var skFlag)
                                                   && skFlag == "1";
                                }

                                // Nota: il modello richiede DataType; qui si usa "string" di default.
                                var prop = new ReportProperty
                                {
                                    Name = kv.Key ?? string.Empty,
                                    Value = kv.Value ?? string.Empty,
                                    ReportEntity = entity,
                                    Unit = string.Equals(kv.Key, "value", StringComparison.OrdinalIgnoreCase) ? unit : null,
                                    DataType = dataType,
                                    IsSubjectKey = isSubjectKey
                                };

                                entity.Properties.Add(prop);
                                propertiesSaved++;
                            }
                        }

                        reportFile.Entities.Add(entity);
                        entitiesSaved++;
                    }
                }

                db.ReportFiles.Add(reportFile);
                await db.SaveChangesAsync();

                await tran.CommitAsync();

                logger?.Invoke($"[Data] Salvate nel DB: ReportFileId={reportFile.Id}, Entities={entitiesSaved}, Properties={propertiesSaved}.");
                return (reportFile.Id, entitiesSaved, propertiesSaved);
            }
            catch
            {
                try { await tran.RollbackAsync(); } catch { }
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Chiave naturale duplicato: stesso FullPath nello stesso ReportType.
        // FullPath identifica univocamente il file su disco;
        // ReportTypeId garantisce che due plugin diversi sullo stesso file
        // fisico non si ostacolino a vicenda.
        // ----------------------------------------------------------------

        /// <summary>
        /// Restituisce il numero di ReportFile già presenti in DB con lo stesso
        /// FullPath e ReportTypeKey. Usato dal chiamante per decidere se aprire
        /// il dialog di gestione duplicato prima di chiamare SaveAsync.
        /// </summary>
        public static async Task<int> CountExistingAsync(
            string fileName,
            string reportTypeKey,
            RRDADbContext db)
        {
            return await db.ReportFiles
                           .AsNoTracking()
                           .CountAsync(f =>
                               f.FileName == fileName &&
                               f.ReportType.Key == reportTypeKey);
        }
    }
}