using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Areas.Data.Controllers;
using System.Globalization;

namespace RRDA.Web.Services.TypePivot;

public sealed class TypePivotDatasetService(RRDADbContext db) : ITypePivotDatasetService
{
    public async Task<TypePivotDataset?> GetAsync(
        TypePivotFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        var reportType = await db.ReportTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.ReportTypeId, cancellationToken);

        if (reportType is null)
            return null;

        var batchRecords = await db.ReportBatches
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name, b.Description })
            .ToListAsync(cancellationToken);

        var filesQuery = db.ReportFiles
            .AsNoTracking()
            .Where(f => f.ReportTypeId == request.ReportTypeId);

        if (request.BatchIds.Count > 0)
            filesQuery = filesQuery.Where(f => f.ReportBatchId.HasValue
                && request.BatchIds.Contains(f.ReportBatchId.Value));
        if (request.LastModifiedFrom.HasValue)
            filesQuery = filesQuery.Where(f => f.FileLastModify >= request.LastModifiedFrom.Value);
        if (request.LastModifiedTo.HasValue)
            filesQuery = filesQuery.Where(f => f.FileLastModify <= request.LastModifiedTo.Value);

        var baseFileIds = await filesQuery
            .OrderByDescending(f => f.FileLastModify)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        var filteredFileIds = baseFileIds;
        var metadata = await BuildMetadataAsync(baseFileIds, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.FilterField)
            && (!string.IsNullOrWhiteSpace(request.FilterFrom) || !string.IsNullOrWhiteSpace(request.FilterTo)))
        {
            if (metadata.FilterFieldDataTypes.TryGetValue(request.FilterField, out var dataType))
            {
                var pairs = await LoadPairsAsync(
                    baseFileIds,
                    isSubjectKey: false,
                    entityKey: request.FilterField,
                    cancellationToken);
                var allowedFileIds = pairs
                    .Where(p => MatchesFilter(p.Value, dataType, request.FilterFrom, request.FilterTo))
                    .Select(p => p.FileId)
                    .ToHashSet();
                filteredFileIds = [.. filteredFileIds.Where(allowedFileIds.Contains)];
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectKeyFrom)
            || !string.IsNullOrWhiteSpace(request.SubjectKeyTo))
        {
            var pairs = await LoadPairsAsync(
                filteredFileIds,
                isSubjectKey: true,
                entityKey: null,
                cancellationToken);
            var allowedFileIds = pairs
                .Where(p => MatchesNumericRange(p.Value, request.SubjectKeyFrom, request.SubjectKeyTo))
                .Select(p => p.FileId)
                .ToHashSet();
            filteredFileIds = [.. filteredFileIds.Where(allowedFileIds.Contains)];
        }

        return new TypePivotDataset
        {
            ReportType = reportType,
            FileIds = filteredFileIds,
            BatchOptions = batchRecords.Select(b => new TypePivotBatchOption
            {
                Id = b.Id,
                Label = string.IsNullOrWhiteSpace(b.Description) ? b.Name : $"{b.Name} - {b.Description}"
            }).ToList(),
            BatchNames = batchRecords.ToDictionary(b => b.Id, b => b.Name),
            Metadata = metadata
        };
    }

    private async Task<TypePivotMetadata> BuildMetadataAsync(
        List<int> fileIds,
        CancellationToken cancellationToken)
    {
        var pairs = await LoadPairsAsync(
            fileIds,
            isSubjectKey: null,
            entityKey: null,
            cancellationToken);
        var subjectKeyPairs = pairs.Where(p => p.IsSubjectKey).ToList();
        var measurePairs = pairs.Where(p => !p.IsSubjectKey).ToList();
        var scalarPairs = measurePairs.Where(p => !p.IsRange).ToList();
        var rangePairs = measurePairs.Where(p => p.IsRange).ToList();
        var referenceHeaders = fileIds.Count == 0
            ? []
            : await db.ReportReferences
                .AsNoTracking()
                .Where(reference => fileIds.Contains(reference.SourceReportFileId)
                    && reference.SourceReportEntityId.HasValue)
                .Select(reference => reference.SourceReportEntity!.Key)
                .Distinct()
                .OrderBy(key => key)
                .ToListAsync(cancellationToken);
        var referenceHeaderSet = referenceHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleScalarPairs = scalarPairs
            .Where(pair => pair.VisibleInPivot ?? IsNumericDataType(pair.DataType))
            .ToList();
        var visibleReferenceHeaders = referenceHeaders
            .Where(header => visibleScalarPairs.Any(pair => string.Equals(pair.Key, header, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var statisticalHeaders = scalarPairs
            .Where(p => (p.VisibleInPivot ?? true) && IsNumericDataType(p.DataType) && !referenceHeaderSet.Contains(p.Key))
            .Select(p => p.Key)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var visibleHeaders = visibleScalarPairs
            .Where(pair => !referenceHeaderSet.Contains(pair.Key))
            .Select(pair => pair.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Concat(visibleReferenceHeaders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rangeHeaders = TypePivotRangeAggregator.BuildDescriptors(
            rangePairs.Where(p => (p.VisibleInPivot ?? true) && IsNumericDataType(p.DataType)));
        var plotXAxisFields = new List<string> { "FileName", "LastModified", "Batch" };
        if (subjectKeyPairs.Count > 0)
            plotXAxisFields.Insert(0, "SubjectKey");

        return new TypePivotMetadata
        {
            AllMeasureHeaders = scalarPairs
                .Where(p => !referenceHeaderSet.Contains(p.Key))
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FilterFieldDataTypes = scalarPairs
                .Where(pair => pair.Filterable == true && !referenceHeaderSet.Contains(pair.Key))
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(pair => pair.DataType).FirstOrDefault(type => !string.IsNullOrWhiteSpace(type)) ?? "string",
                    StringComparer.OrdinalIgnoreCase),
            VisibleHeaders = visibleHeaders,
            ReferenceHeaders = visibleReferenceHeaders,
            StatisticalHeaders = statisticalHeaders,
            ExpandedRangeHeaders = rangeHeaders.SelectMany(range => range.ExpandedHeaders).ToList(),
            RangeHeaders = rangeHeaders,
            HeaderUnits = measurePairs
                .Where(p => IsNumericDataType(p.DataType) && !referenceHeaderSet.Contains(p.Key))
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => p.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                    StringComparer.OrdinalIgnoreCase),
            HasSubjectKey = subjectKeyPairs.Count > 0,
            SubjectKeyLabel = subjectKeyPairs.FirstOrDefault()?.Key ?? "SubjectKey",
            PlotXAxisFields = plotXAxisFields
        };
    }

    private async Task<List<PivotPair>> LoadPairsAsync(
        List<int> fileIds,
        bool? isSubjectKey,
        string? entityKey,
        CancellationToken cancellationToken)
    {
        if (fileIds.Count == 0)
            return [];

        return await db.ReportEntities
            .AsNoTracking()
            .Where(e => fileIds.Contains(e.ReportFileId)
                && (entityKey == null || e.Key == entityKey))
            .SelectMany(e => e.Properties
                .Where(p => p.Name == "value"
                    && (!isSubjectKey.HasValue || p.IsSubjectKey == isSubjectKey.Value))
                .Select(p => new PivotPair
                {
                    FileId = e.ReportFileId,
                    Key = e.Key,
                    Value = p.Value,
                    IsSubjectKey = p.IsSubjectKey,
                    DataType = p.DataType,
                    Unit = p.Unit,
                    RangeName = e.Properties.Where(x => x.Name == "name").Select(x => x.Value).FirstOrDefault(),
                    RowIndex = e.Properties.Where(x => x.Name == "row_index").Select(x => x.Value).FirstOrDefault(),
                    ColIndex = e.Properties.Where(x => x.Name == "col_index").Select(x => x.Value).FirstOrDefault(),
                    VisibleInPivotValue = e.Properties.Where(x => x.Name == "visible_in_pivot").Select(x => x.Value).FirstOrDefault(),
                    FilterableValue = e.Properties.Where(x => x.Name == "filterable").Select(x => x.Value).FirstOrDefault()
                }))
            .ToListAsync(cancellationToken);
    }

    private static bool IsNumericDataType(string? dataType) =>
        string.Equals(dataType, "int", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dataType, "double", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFilter(string? rawValue, string dataType, string? filterFrom, string? filterTo)
    {
        if (string.Equals(dataType, "string", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(filterFrom)
                && (rawValue?.Contains(filterFrom, StringComparison.OrdinalIgnoreCase) ?? false);
        if (string.Equals(dataType, "bool", StringComparison.OrdinalIgnoreCase))
            return bool.TryParse(filterFrom, out var expected)
                && (bool.TryParse(rawValue, out var actual) && actual == expected);
        if (string.Equals(dataType, "datetime", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
                return false;
            var dateFrom = DateTime.TryParse(filterFrom, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedFrom) ? parsedFrom : (DateTime?)null;
            var dateTo = DateTime.TryParse(filterTo, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedTo) ? parsedTo : (DateTime?)null;
            return (!dateFrom.HasValue || date >= dateFrom.Value) && (!dateTo.HasValue || date <= dateTo.Value);
        }
        return MatchesNumericRange(rawValue, filterFrom, filterTo);
    }

    private static bool MatchesNumericRange(string? rawValue, string? rawFrom, string? rawTo)
    {
        var value = TryParseDouble(rawValue);
        if (!value.HasValue)
            return false;

        var from = TryParseDouble(rawFrom);
        var to = TryParseDouble(rawTo);
        return (!from.HasValue || value.Value >= from.Value)
            && (!to.HasValue || value.Value <= to.Value);
    }

    private static double? TryParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
