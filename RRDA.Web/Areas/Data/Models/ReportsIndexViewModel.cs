using RRDA.Core;

namespace RRDA.Web.Areas.Data.Models;

public sealed class ReportsIndexViewModel
{
    public required IReadOnlyList<ReportTypeCardViewModel> ReportTypes { get; init; }
}

public sealed class ReportTypeCardViewModel
{
    public int Id { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public ReportSubjectKind SubjectKind { get; init; }
    public int ReportCount { get; init; }
}
