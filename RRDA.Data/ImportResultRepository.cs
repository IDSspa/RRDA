using Microsoft.EntityFrameworkCore;
using RRDA.Core;

namespace RRDA.Data
{
    /// <summary>
    /// Repository per la persistenza del risultato di import.
    /// Implementazione EF Core (usa il modello RRDADbContext).
    /// </summary>
    public sealed class ImportResultRepository(
        IDbContextFactory<RRDADbContext> dbFactory) : IImportResultRepository
    {
        /// <summary>
        /// Salva ImportResult usando un DbContext creato e gestito dal repository.
        /// Restituisce il riepilogo dei dati salvati.
        /// </summary>
        public async Task<ImportSaveResult> SaveAsync(
            ImportFileItem file,
            ImportResult importResult,
            Action<string>? logger = null,
            string? user = null,
            int? batchId = null,
            DuplicateImportStrategy duplicateStrategy = DuplicateImportStrategy.NewVersion,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(importResult);
            
            if (string.IsNullOrWhiteSpace(file.Name))
                throw new ArgumentException("FileName non valido.", nameof(file));

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // transazione EF
            await using var tran = await db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Recupera il ReportType tramite la Key (colonna Key)
                var reportType = await db.ReportTypes
                                         .AsTracking()
                                         .FirstOrDefaultAsync(rt => rt.Key == importResult.ReportTypeKey, cancellationToken) ?? throw new InvalidOperationException($"ReportType con chiave '{importResult.ReportTypeKey}' non trovato in ReportTypes.");
                var importedEntities = importResult.Entities?.ToList() ?? [];

                // -- Gestione duplicato --
                var existingFiles = await db.ReportFiles
                                            .Where(f =>
                                                f.FileName == file.Name &&
                                                f.ReportTypeId == reportType.Id)
                                            .ToListAsync(cancellationToken);

                if (existingFiles.Count > 0) {
                    switch (duplicateStrategy)
                    {
                        case DuplicateImportStrategy.Block:
                            // Interrompe senza toccare il DB; il chiamante intercetta
                            // l'eccezione come segnale di import bloccato.
                            throw new DuplicateImportException(file.Name, existingFiles.Count);

                        case DuplicateImportStrategy.Replace:
                            // DELETE in cascata di tutti i ReportFile esistenti per questo
                            // nome file + ReportType. Il cascade su ReportEntities e
                            // ReportProperties è configurato in OnModelCreating.
                            var existingFileIds = existingFiles.Select(existing => existing.Id).ToList();
                            var incomingManualReferences = await db.ReportReferences
                                .Where(reference => reference.TargetReportFileId.HasValue
                                    && existingFileIds.Contains(reference.TargetReportFileId.Value))
                                .ToListAsync(cancellationToken);
                            db.ReportReferences.RemoveRange(incomingManualReferences);
                            db.ReportFiles.RemoveRange(existingFiles);
                            await db.SaveChangesAsync(cancellationToken);
                            logger?.Invoke(
                                $"[Data] Replace: eliminati {existingFiles.Count} ReportFile esistenti per '{file.Name}'.");
                            break;

                        case DuplicateImportStrategy.NewVersion:
                            // Nessuna azione: procede con l'insert affiancato.
                            logger?.Invoke(
                                $"[Data] NewVersion: mantenuti {existingFiles.Count} ReportFile esistenti per '{file.Name}', aggiunto nuovo.");
                            break;
                    }
                }

                var reportFile = new ReportFile
                {
                    FileName = file.Name,
                    FullPath = file.FullPath,
                    UploadedAt = DateTime.UtcNow,
                    ReportTypeId = reportType.Id,
                    ReportType = reportType,
                    ImportedBy = user,
                    ReportBatchId = batchId,
                    Entities = [],
                    FileLastModify = file.LastWriteTime
                };

                var referencedTypeKeys = importedEntities
                    .Where(dto => dto.Reference is not null)
                    .Select(dto => dto.Reference!.TargetReportTypeKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var referencedTypes = await db.ReportTypes
                    .Where(type => referencedTypeKeys.Contains(type.Key))
                    .ToListAsync(cancellationToken);
                var referencedTypesByKey = referencedTypes
                    .ToDictionary(type => type.Key, StringComparer.OrdinalIgnoreCase);
                var missingReferencedTypes = referencedTypeKeys
                    .Where(key => !referencedTypesByKey.ContainsKey(key))
                    .ToList();
                if (missingReferencedTypes.Count > 0)
                {
                    throw new InvalidDataException(
                        "I riferimenti importati indicano ReportTypes non registrati: "
                        + string.Join(", ", missingReferencedTypes));
                }

                var entitiesSaved = 0;
                var propertiesSaved = 0;

                if (importedEntities.Count > 0)
                {
                    foreach (var dto in importedEntities)
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
                        if (dto.Reference is not null)
                        {
                            db.ReportReferences.Add(new ReportReference
                            {
                                SourceReportFile = reportFile,
                                SourceReportEntity = entity,
                                TargetReportType = referencedTypesByKey[dto.Reference.TargetReportTypeKey],
                                TargetKeyField = dto.Reference.TargetKeyField,
                                TargetKeyValue = string.IsNullOrWhiteSpace(dto.Reference.TargetKeyValue)
                                    ? null
                                    : dto.Reference.TargetKeyValue.Trim(),
                                Origin = ReportReferenceOrigin.Imported,
                                CreatedAtUtc = DateTime.UtcNow,
                                CreatedBy = user
                            });
                        }
                        entitiesSaved++;
                    }
                }

                db.ReportFiles.Add(reportFile);
                await db.SaveChangesAsync(cancellationToken);

                await tran.CommitAsync(cancellationToken);

                logger?.Invoke($"[Data] Salvate nel DB: ReportFileId={reportFile.Id}, Entities={entitiesSaved}, Properties={propertiesSaved}.");
                return new ImportSaveResult(reportFile.Id, entitiesSaved, propertiesSaved);
            }
            catch
            {
                try { await tran.RollbackAsync(); } catch { }
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Chiave naturale duplicato: stesso nome file nello stesso ReportType.
        // Il percorso non può essere usato perché gli import possono provenire
        // da client RepImp diversi oppure da un upload Web.
        // ----------------------------------------------------------------

        /// <summary>
        /// Restituisce il numero di ReportFile già presenti in DB con lo stesso
        /// nome file e ReportTypeKey. Usato dal chiamante per decidere se aprire
        /// il dialog di gestione duplicato prima di chiamare SaveAsync.
        /// </summary>
        public async Task<int> CountExistingAsync(
            string fileName,
            string reportTypeKey,
            CancellationToken cancellationToken = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            return await db.ReportFiles
                           .AsNoTracking()
                           .CountAsync(f =>
                               f.FileName == fileName &&
                               f.ReportType.Key == reportTypeKey,
                               cancellationToken);
        }
    }
}
