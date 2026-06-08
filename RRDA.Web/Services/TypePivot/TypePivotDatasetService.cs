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

        if (request.BatchId.HasValue)
            filesQuery = filesQuery.Where(f => f.ReportBatchId == request.BatchId.Value);
        if (request.LastModifiedFrom.HasValue)
            filesQuery = filesQuery.Where(f => f.FileLastModify >= request.LastModifiedFrom.Value);
        if (request.LastModifiedTo.HasValue)
            filesQuery = filesQuery.Where(f => f.FileLastModify <= request.LastModifiedTo.Value);

        var fileIds = await filesQuery
            .OrderByDescending(f => f.FileLastModify)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.FilterField)
            && (!string.IsNullOrWhiteSpace(request.FilterFrom) || !string.IsNullOrWhiteSpace(request.FilterTo)))
        {
            var pairs = await LoadPairsAsync(
                fileIds,
                isSubjectKey: false,
                entityKey: request.FilterField,
                cancellationToken);
            var allowedFileIds = pairs
                .Where(p => IsValueInRange(p.Value, request.FilterFrom, request.FilterTo))
                .Select(p => p.FileId)
                .ToHashSet();
            fileIds = [.. fileIds.Where(allowedFileIds.Contains)];
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectKeyFrom)
            || !string.IsNullOrWhiteSpace(request.SubjectKeyTo))
        {
            var pairs = await LoadPairsAsync(
                fileIds,
                isSubjectKey: true,
                entityKey: null,
                cancellationToken);
            var allowedFileIds = pairs
                .Where(p => IsValueInRange(p.Value, request.SubjectKeyFrom, request.SubjectKeyTo))
                .Select(p => p.FileId)
                .ToHashSet();
            fileIds = [.. fileIds.Where(allowedFileIds.Contains)];
        }

        return new TypePivotDataset
        {
            ReportType = reportType,
            FileIds = fileIds,
            BatchOptions = batchRecords.Select(b => new TypePivotBatchOption
            {
                Id = b.Id,
                Label = string.IsNullOrWhiteSpace(b.Description) ? b.Name : $"{b.Name} - {b.Description}"
            }).ToList(),
            BatchNames = batchRecords.ToDictionary(b => b.Id, b => b.Name),
            Metadata = await BuildMetadataAsync(fileIds, cancellationToken)
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
        var visibleHeaders = scalarPairs
            .Where(p => IsNumericDataType(p.DataType))
            .Select(p => p.Key)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rangeHeaders = rangePairs
            .Where(p => IsNumericDataType(p.DataType) && !string.IsNullOrWhiteSpace(p.RangeName))
            .GroupBy(p => p.RangeName!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TypePivotRangeDescriptor
            {
                Name = group.Key,
                Unit = group.Select(p => p.Unit).FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit)),
                ExpandedHeaders = group.OrderBy(TypePivotRangeAggregator.GetIndex)
                    .Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderBy(range => range.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var plotXAxisFields = new List<string> { "FileName", "LastModified", "Batch" };
        if (subjectKeyPairs.Count > 0)
            plotXAxisFields.Insert(0, "SubjectKey");

        return new TypePivotMetadata
        {
            AllMeasureHeaders = scalarPairs
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            VisibleHeaders = visibleHeaders,
            ExpandedRangeHeaders = rangeHeaders.SelectMany(range => range.ExpandedHeaders).ToList(),
            RangeHeaders = rangeHeaders,
            HeaderUnits = measurePairs
                .Where(p => IsNumericDataType(p.DataType))
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
                    ColIndex = e.Properties.Where(x => x.Name == "col_index").Select(x => x.Value).FirstOrDefault()
                }))
            .ToListAsync(cancellationToken);
    }

    private static bool IsNumericDataType(string? dataType) =>
        string.Equals(dataType, "int", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dataType, "double", StringComparison.OrdinalIgnoreCase);

    private static bool IsValueInRange(string? rawValue, string? rawFrom, string? rawTo)
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
