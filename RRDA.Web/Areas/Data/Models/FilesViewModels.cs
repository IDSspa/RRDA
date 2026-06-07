using Microsoft.AspNetCore.Mvc.Rendering;
using RRDA.Data;

namespace RRDA.Web.Areas.Data.Models;

public sealed class FilesIndexViewModel
{
    public required IReadOnlyList<ReportFile> Files { get; init; }
    public required IReadOnlyList<SelectListItem> ReportTypeOptions { get; init; }
    public required IReadOnlyList<SelectListItem> BatchOptions { get; init; }
    public required FilesIndexFilters Filters { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public int CurrentPage { get; init; }
}

public sealed class FilesIndexFilters
{
    public int? ReportTypeId { get; init; }
    public int? BatchId { get; init; }
    public string? ImportedBy { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public int PageSize { get; init; }
}

public sealed class FileEntitiesViewModel
{
    public required ReportFile File { get; init; }
    public required IReadOnlyList<ReportEntity> Entities { get; init; }
    public required IReadOnlyList<string> Kinds { get; init; }
    public string? KindFilter { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public int CurrentPage { get; init; }
}

