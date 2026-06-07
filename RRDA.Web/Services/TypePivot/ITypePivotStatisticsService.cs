using RRDA.Web.Areas.Data.Controllers;

namespace RRDA.Web.Services.TypePivot;

public interface ITypePivotStatisticsService
{
    Task<Dictionary<string, TypePivotColumnStatistics>> GetAsync(
        IReadOnlyCollection<int> fileIds,
        IReadOnlyCollection<string> visibleHeaders,
        CancellationToken cancellationToken = default);
}
