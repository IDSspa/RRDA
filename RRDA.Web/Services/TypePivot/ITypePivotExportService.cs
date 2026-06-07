namespace RRDA.Web.Services.TypePivot;

public interface ITypePivotExportService
{
    Task<TypePivotExportResult?> ExportAsync(
        TypePivotExportRequest request,
        CancellationToken cancellationToken = default);
}

