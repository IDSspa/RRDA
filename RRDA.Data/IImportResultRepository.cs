using RRDA.Core;

namespace RRDA.Data;

public interface IImportResultRepository
{
    Task<ImportSaveResult> SaveAsync(
        ImportFileItem file,
        ImportResult importResult,
        RRDADbContext db,
        Action<string>? logger = null,
        string? user = null,
        int? batchId = null,
        DuplicateImportStrategy duplicateStrategy = DuplicateImportStrategy.NewVersion,
        CancellationToken cancellationToken = default);

    Task<int> CountExistingAsync(
        string fileName,
        string reportTypeKey,
        RRDADbContext db,
        CancellationToken cancellationToken = default);
}

public sealed record ImportFileItem(
    string Name,
    long Length,
    DateTime LastWriteTime,
    string Type,
    string FullPath);

public sealed record ImportSaveResult(
    int ReportFileId,
    int EntitiesSaved,
    int PropertiesSaved);
