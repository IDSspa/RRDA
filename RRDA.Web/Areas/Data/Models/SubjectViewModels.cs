using RRDA.Data;

namespace RRDA.Web.Areas.Data.Models;

public sealed class TabularSubjectViewModel
{
    public required ReportType ReportType { get; init; }
    public required IReadOnlyList<TabularPreviewRow> Rows { get; init; }
}

public sealed class TabularPreviewRow
{
    public int EntityId { get; init; }
    public string EntityKey { get; init; } = string.Empty;
    public string ReportSheet { get; init; } = string.Empty;
    public int PropertiesCount { get; init; }
}
