using RRDA.Web.Areas.Data.Controllers;

namespace RRDA.Web.Services.TypePivot;

public interface ITypePivotViewModelBuilder
{
    Task<TypePivotViewModel?> BuildAsync(
        TypePivotViewRequest request,
        CancellationToken cancellationToken = default);
}

