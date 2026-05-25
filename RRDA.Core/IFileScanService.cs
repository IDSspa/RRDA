namespace RRDA.Core
{
    public interface IFileScanService
    {
        Task<IReadOnlyList<ScannedReportFile>> ScanAsync(
        FileScanRequest request,
        IReadOnlyCollection<IReportImporter> importers,
        IProgress<FileScanProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default);
    }

    public sealed record FileScanRequest(
    string RootFolder,
    string SearchPattern,
    int MaxDepth);

    public sealed record ScannedReportFile(
        string Name,
        string FullPath,
        long Length,
        DateTime LastWriteTime,
        string? ReportType,
        string? MatchedPluginName);

    public sealed record FileScanProgress(
        FileScanPhase Phase,
        int? ProcessedItems,
        int? TotalItems,
        string? CurrentPath,
        string? Message);

    public enum FileScanPhase
    {
        ScanningDirectories,
        ClassifyingFiles,
        Completed
    }
}
