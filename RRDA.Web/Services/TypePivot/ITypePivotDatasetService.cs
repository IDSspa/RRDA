namespace RRDA.Web.Services.TypePivot;

public interface ITypePivotDatasetService
{
    Task<TypePivotDataset?> GetAsync(
        TypePivotFilterRequest request,
        CancellationToken cancellationToken = default);
}
