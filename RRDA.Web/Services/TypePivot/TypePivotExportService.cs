using Microsoft.EntityFrameworkCore;
using RRDA.Core.Exporting;
using RRDA.Data;
using RRDA.Web.Areas.Data.Controllers;

namespace RRDA.Web.Services.TypePivot;

public sealed class TypePivotExportService(
    RRDADbContext db,
    IDataExportService exportService,
    ITypePivotDatasetService datasetService) : ITypePivotExportService
{
    public async Task<TypePivotExportResult?> ExportAsync(
        TypePivotExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var dataset = await datasetService.GetAsync(request.Filter, cancellationToken);
        if (dataset is null)
            return null;

        var fileIds = dataset.FileIds;
        var metadata = dataset.Metadata;
        var files = fileIds.Count == 0 ? [] : await db.ReportFiles
            .AsNoTracking()
            .Where(file => fileIds.Contains(file.Id))
            .Select(file => new { file.Id, file.FileName, file.FileLastModify, file.ReportBatchId })
            .ToListAsync(cancellationToken);
        var pairs = fileIds.Count == 0 ? [] : await db.ReportEntities
            .AsNoTracking()
            .Where(entity => fileIds.Contains(entity.ReportFileId))
            .SelectMany(entity => entity.Properties
                .Where(property => property.Name == "value")
                .Select(property => new PivotPair
                {
                    FileId = entity.ReportFileId,
                    Key = entity.Key,
                    Value = property.Value,
                    IsSubjectKey = property.IsSubjectKey,
                    DataType = property.DataType,
                    Unit = property.Unit
                }))
            .ToListAsync(cancellationToken);

        var pairsByFileId = pairs
            .GroupBy(pair => pair.FileId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var fileOrder = fileIds
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index);
        var columns = BuildColumns(metadata);
        var rows = files
            .OrderBy(file => fileOrder[file.Id])
            .Select(file => BuildRow(file.Id, file.FileName, file.FileLastModify, file.ReportBatchId))
            .ToList();
        var document = exportService.Export(new DataExportTable(columns, rows), request.Format);
        var fileName = $"RRDA_{dataset.ReportType.Key}_{DateTime.Now:yyyyMMdd_HHmmss}{document.FileExtension}";
        return new TypePivotExportResult(document, fileName);

        IReadOnlyDictionary<string, object?> BuildRow(
            int fileId,
            string fileName,
            DateTime lastModified,
            int? batchId)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Batch"] = batchId.HasValue && dataset.BatchNames.TryGetValue(batchId.Value, out var batchName)
                    ? batchName
                    : null,
                ["FileName"] = fileName,
                ["LastModified"] = lastModified
            };

            if (pairsByFileId.TryGetValue(fileId, out var filePairs))
            {
                foreach (var pair in filePairs)
                {
                    if (pair.IsSubjectKey)
                        row["SubjectKey"] = pair.Value;
                    else if (metadata.VisibleHeaders.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                        row[pair.Key] = pair.Value;
                }
            }

            return row;
        }
    }

    private static List<DataExportColumn> BuildColumns(TypePivotMetadata metadata)
    {
        var columns = new List<DataExportColumn>
        {
            new("Batch", "Batch"),
            new("FileName", "Nome file"),
            new("LastModified", "Ultima modifica")
        };

        if (metadata.HasSubjectKey)
            columns.Insert(0, new DataExportColumn("SubjectKey", metadata.SubjectKeyLabel));

        columns.AddRange(metadata.VisibleHeaders.Select(header =>
            new DataExportColumn(header, FormatHeaderLabel(header, metadata.HeaderUnits))));
        return columns;
    }

    private static string FormatHeaderLabel(string header, Dictionary<string, string?> headerUnits) =>
        headerUnits.TryGetValue(header, out var unit) && !string.IsNullOrWhiteSpace(unit)
            ? $"{header} [{unit}]"
            : header;
}
