using RRDA.Core;

namespace RRDA.Data;

public interface IImportResultRepository
{
    Task<ImportSaveResult> SaveAsync(
        ImportFileItem file,
        ImportResult importResult,
        Action<string>? logger = null,
        string? user = null,
        int? batchId = null,
        DuplicateImportStrategy duplicateStrategy = DuplicateImportStrategy.NewVersion,
        CancellationToken cancellationToken = default);

    Task<int> CountExistingAsync(
        string fileName,
        string reportTypeKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportDuplicateMatch>> FindExistingAsync(
        IEnumerable<ImportDuplicateCandidate> candidates,
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

public sealed record ImportDuplicateCandidate(string FileName, string ReportTypeKey);

public sealed record ImportDuplicateMatch(string FileName, string ReportTypeKey, int ExistingCount);
