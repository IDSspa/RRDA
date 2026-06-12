using Microsoft.AspNetCore.Mvc.Rendering;
using RRDA.Data;

namespace RRDA.Web.Areas.Data.Models;

public sealed class FileDetailsViewModel
{
    public required ReportFile File { get; init; }
    public required IReadOnlyList<FileEntityKindViewModel> EntityKinds { get; init; }
    public int EntityCount { get; init; }
    public IReadOnlyList<FileReferenceViewModel> References { get; init; } = [];
    public IReadOnlyList<SelectListItem> ManualReferenceTargets { get; init; } = [];
    public bool CanManageReferences { get; init; }
}

public sealed class FileEntityKindViewModel
{
    public required string Kind { get; init; }
    public int Count { get; init; }
}

public sealed class FileReferenceViewModel
{
    public int Id { get; init; }
    public ReportReferenceOrigin Origin { get; init; }
    public bool IsIncoming { get; init; }
    public string? SourceField { get; init; }
    public string? TargetReportTypeKey { get; init; }
    public string? TargetKeyField { get; init; }
    public string? TargetKeyValue { get; init; }
    public IReadOnlyList<ReportReferenceTargetViewModel> Targets { get; init; } = [];
}

public sealed class ReportReferenceTargetViewModel
{
    public int FileId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ReportTypeKey { get; init; } = string.Empty;
}
