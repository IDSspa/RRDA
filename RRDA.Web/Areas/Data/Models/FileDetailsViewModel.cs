using RRDA.Data;

namespace RRDA.Web.Areas.Data.Models;

public sealed class FileDetailsViewModel
{
    public required ReportFile File { get; init; }
    public required IReadOnlyList<FileEntityKindViewModel> EntityKinds { get; init; }
    public int EntityCount { get; init; }
}

public sealed class FileEntityKindViewModel
{
    public required string Kind { get; init; }
    public int Count { get; init; }
}
