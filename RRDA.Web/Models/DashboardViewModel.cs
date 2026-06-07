using RRDA.Data;

namespace RRDA.Web.Models;

public sealed class DashboardViewModel
{
    public int TotalFiles { get; init; }
    public int TotalBatches { get; init; }
    public int TotalEntities { get; init; }
    public int TotalReportTypes { get; init; }
    public int TotalUsers { get; init; }
    public required IReadOnlyList<ReportFile> RecentFiles { get; init; }
}

