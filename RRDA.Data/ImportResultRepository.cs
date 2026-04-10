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
        public static async Task<(int ReportFileId, int EntitiesSaved, int PropertiesSaved)> SaveAsync(
            string fileName,
            string fullPath,
            ImportResult importResult,
            RRDADbContext db,
            Action<string>? logger = null,
            string? user = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (importResult == null) throw new ArgumentNullException(nameof(importResult));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("FileName non valido.", nameof(fileName));

            // transazione EF
            await using var tran = await db.Database.BeginTransactionAsync();

            try
            {
                // Recupera il ReportType tramite la Key (colonna Key)
                var reportType = await db.ReportTypes
                                         .AsTracking()
                                         .FirstOrDefaultAsync(rt => rt.Key == importResult.ReportTypeKey) ?? throw new InvalidOperationException($"ReportType con chiave '{importResult.ReportTypeKey}' non trovato in ReportTypes.");
                
                var reportFile = new ReportFile
                {
                    FileName = fileName,
                    FullPath = fullPath,
                    UploadedAt = DateTime.UtcNow,
                    ReportTypeId = reportType.Id,
                    ReportType = reportType,
                    ImportedBy = user,
                    Entities = new List<ReportEntity>()
                };

                var entitiesSaved = 0;
                var propertiesSaved = 0;

                if (importResult.Entities != null && importResult.Entities.Any())
                {
                    foreach (var dto in importResult.Entities)
                    {
                        var entity = new ReportEntity
                        {
                            EntityKind = dto.EntityKind ?? string.Empty,
                            Key = dto.Key ?? string.Empty,
                            ReportFile = reportFile,
                            Properties = new List<ReportProperty>()
                        };

                        if (dto.Properties != null && dto.Properties.Count > 0)
                        {
                            foreach (var kv in dto.Properties)
                            {
                                // Nota: il modello richiede DataType; qui si usa "string" di default.
                                var prop = new ReportProperty
                                {
                                    Name = kv.Key ?? string.Empty,
                                    Value = kv.Value ?? string.Empty,
                                    ReportEntity = entity,
                                    Unit = null,
                                    DataType = "string"
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
    }
}