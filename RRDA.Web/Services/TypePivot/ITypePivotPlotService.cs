namespace RRDA.Web.Services.TypePivot;

public interface ITypePivotPlotService
{
    Task<TypePivotPlotResult> BuildAsync(
        TypePivotPlotRequest request,
        CancellationToken cancellationToken = default);
}
