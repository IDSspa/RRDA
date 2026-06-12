using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Areas.Data.Controllers;
using RRDA.Web.Services;

namespace RRDA.Web.Services.TypePivot;

public sealed class TypePivotViewModelBuilder(
    RRDADbContext db,
    ITypePivotDatasetService datasetService,
    ITypePivotOrderingService orderingService) : ITypePivotViewModelBuilder
{
    public async Task<TypePivotViewModel?> BuildAsync(
        TypePivotViewRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = request.PageSize switch { <= 0 => 50, > 200 => 200, _ => request.PageSize };
        var dataset = await datasetService.GetAsync(request.Filter, cancellationToken);
        if (dataset is null)
            return null;

        var fileIds = dataset.FileIds;
        var metadata = dataset.Metadata;
        var totalFiles = fileIds.Count;
        var sortField = metadata.HasSubjectKey
            && (string.IsNullOrWhiteSpace(request.SortField)
                || string.Equals(request.SortField, "SubjectKey", StringComparison.OrdinalIgnoreCase))
            ? "SubjectKey"
            : null;
        var sortDirection = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";

        if (sortField == "SubjectKey")
            fileIds = await orderingService.OrderBySubjectKeyAsync(fileIds, sortDirection, cancellationToken);

        var pageFileIds = fileIds
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        var model = CreateBaseModel(
            request,
            dataset,
            sortField,
            sortDirection,
            page,
            pageSize,
            totalFiles);

        if (pageFileIds.Count == 0)
            return model;

        var files = await db.ReportFiles
            .AsNoTracking()
            .Where(file => pageFileIds.Contains(file.Id))
            .Select(file => new { file.Id, file.FileName, file.FileLastModify, file.ReportBatchId })
            .ToListAsync(cancellationToken);
        var loadedFileIds = files.Select(file => file.Id).ToList();
        var pairs = await db.ReportEntities
            .AsNoTracking()
            .Where(entity => loadedFileIds.Contains(entity.ReportFileId))
            .SelectMany(entity => entity.Properties
                .Where(property => property.Name == "value")
                .Select(property => new PivotPair
                {
                    FileId = entity.ReportFileId,
                    Key = entity.Key,
                    Value = property.Value,
                    IsSubjectKey = property.IsSubjectKey,
                    DataType = property.DataType,
                    Unit = property.Unit,
                    RangeName = entity.Properties.Where(x => x.Name == "name").Select(x => x.Value).FirstOrDefault(),
                    RowIndex = entity.Properties.Where(x => x.Name == "row_index").Select(x => x.Value).FirstOrDefault(),
                    ColIndex = entity.Properties.Where(x => x.Name == "col_index").Select(x => x.Value).FirstOrDefault()
                }))
            .ToListAsync(cancellationToken);

        var rows = files
            .Select(file => new TypePivotRow
            {
                FileId = file.Id,
                FileName = file.FileName,
                LastModified = file.FileLastModify,
                BatchId = file.ReportBatchId,
                BatchName = file.ReportBatchId.HasValue
                    && dataset.BatchNames.TryGetValue(file.ReportBatchId.Value, out var batchName)
                    ? batchName
                    : null
            })
            .ToDictionary(row => row.FileId);

        foreach (var pair in pairs)
        {
            if (!rows.TryGetValue(pair.FileId, out var row))
                continue;

            if (pair.IsSubjectKey)
                row.SubjectKey = pair.Value;
            else if (!pair.IsRange || !request.UseCompactRanges)
                row.Values[pair.Key] = pair.Value;
        }

        await PopulateReferenceTargetsAsync(rows, loadedFileIds, cancellationToken);

        if (request.UseCompactRanges)
        {
            foreach (var fileGroup in pairs.Where(pair => pair.IsRange && !pair.IsSubjectKey && !string.IsNullOrWhiteSpace(pair.RangeName))
                         .GroupBy(pair => pair.FileId))
            {
                if (!rows.TryGetValue(fileGroup.Key, out var row))
                    continue;

                foreach (var rangeGroup in fileGroup.GroupBy(pair => pair.RangeName!, StringComparer.OrdinalIgnoreCase))
                {
                    var cell = TypePivotRangeAggregator.Build(
                        rangeGroup.Key,
                        rangeGroup.Select(pair => pair.Unit).FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit)),
                        rangeGroup);
                    if (cell is not null)
                        row.Ranges[rangeGroup.Key] = cell;
                }
            }
        }

        model.Rows = [.. pageFileIds.Where(rows.ContainsKey).Select(id => rows[id])];
        // ColumnStatistics è intenzionalmente vuoto: viene popolato in modo asincrono
        // dal client tramite l'endpoint TypePivotColumnStats dopo il render della pagina.
        return model;
    }

    private async Task PopulateReferenceTargetsAsync(
        Dictionary<int, TypePivotRow> rows,
        List<int> sourceFileIds,
        CancellationToken cancellationToken)
    {
        var references = await db.ReportReferences
            .AsNoTracking()
            .Where(reference => sourceFileIds.Contains(reference.SourceReportFileId)
                && reference.Origin == ReportReferenceOrigin.Imported)
            .Include(reference => reference.SourceReportEntity)
            .Include(reference => reference.TargetReportType)
            .ToListAsync(cancellationToken);
        references = references
            .Where(reference => reference.SourceReportEntity is not null
                && reference.TargetReportType is not null
                && !string.IsNullOrWhiteSpace(reference.TargetKeyField)
                && !string.IsNullOrWhiteSpace(reference.TargetKeyValue))
            .ToList();
        if (references.Count == 0)
            return;

        var targetTypeIds = references
            .Select(reference => reference.TargetReportType!.Id)
            .Distinct()
            .ToList();
        var targetFields = references
            .Select(reference => reference.TargetKeyField)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candidates = await db.ReportEntities
            .AsNoTracking()
            .Where(entity => targetTypeIds.Contains(entity.ReportFile.ReportTypeId)
                && targetFields.Contains(entity.Key))
            .SelectMany(entity => entity.Properties
                .Where(property => property.Name == "value")
                .Select(property => new
                {
                    entity.ReportFileId,
                    entity.ReportFile.ReportTypeId,
                    entity.ReportFile.UploadedAt,
                    Field = entity.Key,
                    property.Value
                }))
            .ToListAsync(cancellationToken);

        foreach (var reference in references)
        {
            var target = candidates
                .Where(candidate => candidate.ReportTypeId == reference.TargetReportType!.Id
                    && string.Equals(candidate.Field, reference.TargetKeyField, StringComparison.OrdinalIgnoreCase)
                    && ReportReferenceKeyComparer.Equals(candidate.Value, reference.TargetKeyValue))
                .OrderByDescending(candidate => candidate.UploadedAt)
                .FirstOrDefault();
            if (target is not null && rows.TryGetValue(reference.SourceReportFileId, out var row))
                row.ReferenceTargetFileIds[reference.SourceReportEntity!.Key] = target.ReportFileId;
        }
    }

    private static TypePivotViewModel CreateBaseModel(
        TypePivotViewRequest request,
        TypePivotDataset dataset,
        string? sortField,
        string sortDirection,
        int page,
        int pageSize,
        int totalFiles) =>
        new()
        {
            ReportTypeId = dataset.ReportType.Id,
            ReportTypeKey = dataset.ReportType.Key,
            Headers = request.UseCompactRanges
                ? dataset.Metadata.VisibleHeaders
                : [.. dataset.Metadata.VisibleHeaders, .. dataset.Metadata.ExpandedRangeHeaders],
            RangeHeaders = request.UseCompactRanges ? dataset.Metadata.RangeHeaders : [],
            UseCompactRanges = request.UseCompactRanges,
            HeaderUnits = dataset.Metadata.HeaderUnits,
            ReferenceHeaders = dataset.Metadata.ReferenceHeaders,
            DynamicFilterFields = dataset.Metadata.AllMeasureHeaders,
            BatchId = request.Filter.BatchId,
            LastModifiedFrom = request.Filter.LastModifiedFrom,
            LastModifiedTo = request.Filter.LastModifiedTo,
            FilterField = request.Filter.FilterField,
            FilterFrom = request.Filter.FilterFrom,
            FilterTo = request.Filter.FilterTo,
            SubjectKeyFrom = request.Filter.SubjectKeyFrom,
            SubjectKeyTo = request.Filter.SubjectKeyTo,
            SortField = sortField,
            SortDirection = sortDirection,
            BatchOptions = dataset.BatchOptions,
            TotalFiles = totalFiles,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalFiles / (double)pageSize),
            DecimalPlaces = request.DecimalPlaces,
            HasSubjectKey = dataset.Metadata.HasSubjectKey,
            SubjectKeyLabel = dataset.Metadata.SubjectKeyLabel,
            PlotXAxisFields = dataset.Metadata.PlotXAxisFields,
            PlotYAxisFields = dataset.Metadata.StatisticalHeaders
        };
}
