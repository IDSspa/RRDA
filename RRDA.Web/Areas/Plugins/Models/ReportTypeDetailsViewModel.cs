using RRDA.Data;

namespace RRDA.Web.Areas.Plugins.Models;

public sealed class ReportTypeDetailsViewModel
{
    public required ReportType ReportType { get; init; }
    public int FileCount { get; init; }
}

