namespace RRDA.Web.Services.TypePivot;

public interface ITypePivotOrderingService
{
    Task<List<int>> OrderBySubjectKeyAsync(
        IReadOnlyCollection<int> fileIds,
        string sortDirection,
        CancellationToken cancellationToken = default);
}
