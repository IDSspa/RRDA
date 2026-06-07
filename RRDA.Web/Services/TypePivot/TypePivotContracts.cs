using RRDA.Data;
using RRDA.Core.Exporting;
using RRDA.Web.Areas.Data.Controllers;

namespace RRDA.Web.Services.TypePivot;

public sealed record TypePivotFilterRequest(
    int ReportTypeId,
    int? BatchId,
    DateTime? LastModifiedFrom,
    DateTime? LastModifiedTo,
    string? FilterField,
    string? FilterFrom,
    string? FilterTo,
    string? SubjectKeyFrom,
    string? SubjectKeyTo);

public sealed record TypePivotViewRequest(
    TypePivotFilterRequest Filter,
    string? SortField,
    string? SortDirection,
    int Page,
    int PageSize,
    int DecimalPlaces);

public sealed record TypePivotExportRequest(
    TypePivotFilterRequest Filter,
    DataExportFormat Format);

public sealed record TypePivotExportResult(
    DataExportDocument Document,
    string FileName);

public sealed class TypePivotDataset
{
    public required ReportType ReportType { get; init; }
    public required List<int> FileIds { get; init; }
    public required List<TypePivotBatchOption> BatchOptions { get; init; }
    public required Dictionary<int, string> BatchNames { get; init; }
    public required TypePivotMetadata Metadata { get; init; }
}

public sealed class TypePivotMetadata
{
    public List<string> AllMeasureHeaders { get; init; } = [];
    public List<string> VisibleHeaders { get; init; } = [];
    public Dictionary<string, string?> HeaderUnits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasSubjectKey { get; init; }
    public string SubjectKeyLabel { get; init; } = "SubjectKey";
    public List<string> PlotXAxisFields { get; init; } = [];
}

public sealed record TypePivotPlotRequest(
    TypePivotFilterRequest Filter,
    string? ChartType,
    string? XField,
    IReadOnlyCollection<string> SeriesFields,
    IReadOnlyCollection<int> SelectedFileIds);

public enum TypePivotPlotStatus
{
    Success,
    NotFound,
    BadRequest
}

public sealed record TypePivotPlotResult(TypePivotPlotStatus Status, object? Payload);
