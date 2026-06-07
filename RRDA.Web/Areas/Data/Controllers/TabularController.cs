using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RRDA.Core.Exporting;
using RRDA.Data;
using RRDA.Web.Security;
using RRDA.Web.Services.TypePivot;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public class TabularController(
        RRDADbContext db,
        IConfiguration configuration,
        IMemoryCache cache,
        IDataExportService exportService,
        ITypePivotDatasetService typePivotDatasetService,
        ITypePivotPlotService typePivotPlotService) : Controller
    {
        private const string DecimalPlacesCookieName = "RRDA_TypePivot_DecimalPlaces";
        private const int DefaultDecimalPlaces = 4;
        private const int MaxDecimalPlaces = 15;
        private const int StatsCacheDurationMinutes = 10;  // Cache for 10 minutes
        private const string StatsCacheKeyPrefix = "TypePivot_Stats_";

        public async Task<IActionResult> Subject(int reportTypeId)
        {
            var reportType = await db.ReportTypes.FindAsync(reportTypeId);
            if (reportType is null) return NotFound();

            ViewBag.ReportType = reportType;

            var rows = await db.ReportEntities
                .AsNoTracking()
                .Where(e => e.ReportFile.ReportTypeId == reportTypeId)
                .Select(e => new TabularPreviewRow
                {
                    EntityId = e.Id,
                    EntityKey = e.Key,
                    ReportSheet = e.ReportSheet,
                    PropertiesCount = e.Properties.Count
                })
                .Take(200)
                .ToListAsync();

            return View(rows);
        }

        public async Task<IActionResult> FilePivot(int fileId)
        {
            var file = await db.ReportFiles
                .Include(f => f.ReportType)
                .FirstOrDefaultAsync(f => f.Id == fileId);

            if (file is null) return NotFound();

            var entries = await db.ReportEntities
                .AsNoTracking()
                .Where(e => e.ReportFileId == fileId)
                .SelectMany(e => e.Properties
                    .Where(p => p.Name == "value")
                    .Select(p => new { e.Key, p.Value }))
                .ToListAsync();

            var headers = entries
                .Select(x => x.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                row[entry.Key] = entry.Value;

            var model = new FilePivotViewModel
            {
                FileId = file.Id,
                FileName = file.FileName,
                ReportTypeKey = file.ReportType?.Key ?? string.Empty,
                Headers = headers,
                Row = row
            };

            return View(model);
        }

        public async Task<IActionResult> TypePivot(
            int reportTypeId,
            int? batchId,
            DateTime? lastModifiedFrom,
            DateTime? lastModifiedTo,
            string? filterField,
            string? filterFrom,
            string? filterTo,
            string? subjectKeyFrom,
            string? subjectKeyTo,
            string? sortField,
            string? sortDirection,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            // Validazione parametri di paging
            if (page < 1)
                page = 1;

            // Limiti di pageSize: default 50, max 200
            pageSize = pageSize switch { <= 0 => 50, > 200 => 200, _ => pageSize };

            var filterResult = await typePivotDatasetService.GetAsync(new TypePivotFilterRequest(
                reportTypeId,
                batchId,
                lastModifiedFrom,
                lastModifiedTo,
                filterField,
                filterFrom,
                filterTo,
                subjectKeyFrom,
                subjectKeyTo),
                cancellationToken);

            if (filterResult is null)
                return NotFound();

            var reportType = filterResult.ReportType;
            var allFilteredFileIds = filterResult.FileIds;
            var metadata = filterResult.Metadata;
            var totalFiles = allFilteredFileIds.Count;
            var normalizedSortField = string.Equals(sortField, "SubjectKey", StringComparison.OrdinalIgnoreCase)
                ? "SubjectKey"
                : null;
            var normalizedSortDirection = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                ? "desc"
                : "asc";

            if (normalizedSortField == "SubjectKey")
                allFilteredFileIds = await OrderFileIdsBySubjectKeyAsync(allFilteredFileIds, normalizedSortDirection);

            var filesPage = allFilteredFileIds
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            if (filesPage.Count == 0)
            {
                return View(new TypePivotViewModel
                {
                    ReportTypeId = reportType.Id,
                    ReportTypeKey = reportType.Key,
                    TotalFiles = totalFiles,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalFiles / (double)pageSize),
                    DecimalPlaces = ResolveDecimalPlaces(),
                    BatchId = batchId,
                    LastModifiedFrom = lastModifiedFrom,
                    LastModifiedTo = lastModifiedTo,
                    FilterField = filterField,
                    FilterFrom = filterFrom,
                    FilterTo = filterTo,
                    SubjectKeyFrom = subjectKeyFrom,
                    SubjectKeyTo = subjectKeyTo,
                    SortField = normalizedSortField,
                    SortDirection = normalizedSortDirection,
                    BatchOptions = filterResult.BatchOptions,
                    DynamicFilterFields = metadata.AllMeasureHeaders,
                    Headers = metadata.VisibleHeaders,
                    HeaderUnits = metadata.HeaderUnits,
                    HasSubjectKey = metadata.HasSubjectKey,
                    SubjectKeyLabel = metadata.SubjectKeyLabel,
                    PlotXAxisFields = metadata.PlotXAxisFields,
                    PlotYAxisFields = metadata.VisibleHeaders
                });
            }

            // Fetch file details for the current page
            var filesPageDetails = await db.ReportFiles
                .AsNoTracking()
                .Where(f => filesPage.Contains(f.Id))
                .Select(f => new { f.Id, f.FileName, f.FileLastModify, f.ReportBatchId })
                .ToListAsync();

            var pageFileIds = filesPageDetails.Select(f => f.Id).ToList();

            // Query unica: recupera sia SubjectKey sia le misure proiettando IsSubjectKey
            var allPairs = await db.ReportEntities
                .AsNoTracking()
                .Where(e => pageFileIds.Contains(e.ReportFileId))
                .SelectMany(e => e.Properties
                    .Where(p => p.Name == "value")
                    .Select(p => new PivotPair
                    {
                        FileId = e.ReportFileId,
                        Key = e.Key,
                        Value = p.Value,
                        IsSubjectKey = p.IsSubjectKey,
                        DataType = p.DataType,
                        Unit = p.Unit
                    }))
                .ToListAsync();

            // Separazione SubjectKey / misure
            var subjectKeyPairs = allPairs.Where(p => p.IsSubjectKey).ToList();
            var measurePairs = allPairs.Where(p => !p.IsSubjectKey).ToList();

            var visibleHeaders = metadata.VisibleHeaders;
            var visibleHeaderSet = visibleHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // ✅ Use cached method with ALREADY-FILTERED allFilteredFileIds
            var columnStatistics = await GetColumnStatisticsAsync(
                allFilteredFileIds,
                visibleHeaderSet);

            // Costruzione righe
            var rows = filesPageDetails
                .Select(f => new TypePivotRow
                {
                    FileId = f.Id,
                    FileName = f.FileName,
                    LastModified = f.FileLastModify,
                    BatchId = f.ReportBatchId,
                    BatchName = f.ReportBatchId.HasValue
                        && filterResult.BatchNames.TryGetValue(f.ReportBatchId.Value, out var description)
                        ? description
                        : null,
                    SubjectKey = null,
                    Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                })
                .ToDictionary(r => r.FileId);

            foreach (var p in subjectKeyPairs)
                if (rows.TryGetValue(p.FileId, out var row))
                    row.SubjectKey = p.Value;

            foreach (var p in measurePairs)
                if (rows.TryGetValue(p.FileId, out var row))
                    row.Values[p.Key] = p.Value;

            return View(new TypePivotViewModel
            {
                ReportTypeId = reportType.Id,
                ReportTypeKey = reportType.Key,
                Headers = visibleHeaders,
                HeaderUnits = metadata.HeaderUnits,
                ColumnStatistics = columnStatistics,
                DynamicFilterFields = metadata.AllMeasureHeaders,
                BatchId = batchId,
                LastModifiedFrom = lastModifiedFrom,
                LastModifiedTo = lastModifiedTo,
                FilterField = filterField,
                FilterFrom = filterFrom,
                FilterTo = filterTo,
                SubjectKeyFrom = subjectKeyFrom,
                SubjectKeyTo = subjectKeyTo,
                SortField = normalizedSortField,
                SortDirection = normalizedSortDirection,
                BatchOptions = filterResult.BatchOptions,
                Rows = [.. filesPage.Where(rows.ContainsKey).Select(id => rows[id])],
                TotalFiles = totalFiles,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalFiles / (double)pageSize),
                DecimalPlaces = ResolveDecimalPlaces(),
                HasSubjectKey = metadata.HasSubjectKey,
                SubjectKeyLabel = metadata.SubjectKeyLabel,
                PlotXAxisFields = metadata.PlotXAxisFields,
                PlotYAxisFields = metadata.VisibleHeaders
            });
        }

        public async Task<IActionResult> TypePivotExport(
            int reportTypeId,
            int? batchId,
            DateTime? lastModifiedFrom,
            DateTime? lastModifiedTo,
            string? filterField,
            string? filterFrom,
            string? filterTo,
            string? subjectKeyFrom,
            string? subjectKeyTo,
            string? format,
            CancellationToken cancellationToken = default)
        {
            var filterResult = await typePivotDatasetService.GetAsync(new TypePivotFilterRequest(
                reportTypeId,
                batchId,
                lastModifiedFrom,
                lastModifiedTo,
                filterField,
                filterFrom,
                filterTo,
                subjectKeyFrom,
                subjectKeyTo),
                cancellationToken);

            if (filterResult is null)
                return NotFound();

            var fileIds = filterResult.FileIds;
            var metadata = filterResult.Metadata;
            var files = fileIds.Count == 0 ? [] : await db.ReportFiles
                .AsNoTracking()
                .Where(file => fileIds.Contains(file.Id))
                .Select(file => new
                {
                    file.Id,
                    file.FileName,
                    file.FileLastModify,
                    file.ReportBatchId
                })
                .ToListAsync();

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
                .ToListAsync();

            var pairsByFileId = pairs
                .GroupBy(pair => pair.FileId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var fileOrder = fileIds
                .Select((id, index) => new { id, index })
                .ToDictionary(item => item.id, item => item.index);

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

            var rows = files
                .OrderBy(file => fileOrder[file.Id])
                .Select(file =>
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Batch"] = file.ReportBatchId.HasValue
                            && filterResult.BatchNames.TryGetValue(file.ReportBatchId.Value, out var batchName)
                                ? batchName
                                : null,
                        ["FileName"] = file.FileName,
                        ["LastModified"] = file.FileLastModify
                    };

                    if (pairsByFileId.TryGetValue(file.Id, out var filePairs))
                    {
                        foreach (var pair in filePairs)
                        {
                            if (pair.IsSubjectKey)
                                row["SubjectKey"] = pair.Value;
                            else if (metadata.VisibleHeaders.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                                row[pair.Key] = pair.Value;
                        }
                    }

                    return (IReadOnlyDictionary<string, object?>)row;
                })
                .ToList();

            var exportFormat = string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase)
                ? DataExportFormat.Excel
                : DataExportFormat.Csv;
            var document = exportService.Export(new DataExportTable(columns, rows), exportFormat);
            var fileName = $"RRDA_{filterResult.ReportType.Key}_{DateTime.Now:yyyyMMdd_HHmmss}{document.FileExtension}";

            return File(document.Content, document.ContentType, fileName);
        }

        public async Task<IActionResult> TypePivotPlotData(
            int reportTypeId,
            int? batchId,
            DateTime? lastModifiedFrom,
            DateTime? lastModifiedTo,
            string? filterField,
            string? filterFrom,
            string? filterTo,
            string? subjectKeyFrom,
            string? subjectKeyTo,
            string? chartType,
            string? xField,
            [FromQuery] string[] seriesFields,
            [FromQuery] int[] selectedFileIds,
            CancellationToken cancellationToken = default)
        {
            var result = await typePivotPlotService.BuildAsync(
                new TypePivotPlotRequest(
                    new TypePivotFilterRequest(
                        reportTypeId,
                        batchId,
                        lastModifiedFrom,
                        lastModifiedTo,
                        filterField,
                        filterFrom,
                        filterTo,
                        subjectKeyFrom,
                        subjectKeyTo),
                    chartType,
                    xField,
                    seriesFields,
                    selectedFileIds),
                cancellationToken);

            return result.Status switch
            {
                TypePivotPlotStatus.NotFound => NotFound(),
                TypePivotPlotStatus.BadRequest => BadRequest(result.Payload),
                _ => Json(result.Payload)
            };
        }

        private static string FormatHeaderLabel(string header, Dictionary<string, string?> headerUnits)
        {
            return headerUnits.TryGetValue(header, out var unit) && !string.IsNullOrWhiteSpace(unit)
                ? $"{header} [{unit}]"
                : header;
        }

        private int ResolveDecimalPlaces()
        {
            var configured = configuration.GetValue<int?>("TypePivot:DecimalPlaces");
            var fallback = Math.Clamp(configured ?? DefaultDecimalPlaces, 0, MaxDecimalPlaces);

            if (Request.Cookies.TryGetValue(DecimalPlacesCookieName, out var cookieValue)
                && int.TryParse(cookieValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cookiePlaces))
                return Math.Clamp(cookiePlaces, 0, MaxDecimalPlaces);

            return fallback;
        }

        private static TypePivotColumnStatistics CalculateColumnStatistics(IEnumerable<string?> rawValues)
        {
            var values = rawValues
                .Select(TryParseDouble)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();

            if (values.Count == 0)
                return new TypePivotColumnStatistics();

            var mean = values.Average();
            var median = values.Count % 2 == 1
                ? values[values.Count / 2]
                : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
            var variance = values.Count > 1
                ? values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1)
                : 0;

            var max = values.Last();
            var min = values.First();

            return new TypePivotColumnStatistics
            {
                Count = values.Count,
                Mean = mean,
                Median = median,
                StandardDeviation = Math.Sqrt(variance),
                Max = max,
                Min = min
            };
        }

        private static double? TryParseDouble(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue))
                return invariantValue;

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentValue))
                return currentValue;

            return null;
        }

        private async Task<List<int>> OrderFileIdsBySubjectKeyAsync(
            List<int> fileIds,
            string sortDirection)
        {
            if (fileIds.Count == 0)
                return [];

            var subjectKeys = await db.ReportEntities
                .AsNoTracking()
                .Where(entity => fileIds.Contains(entity.ReportFileId))
                .SelectMany(entity => entity.Properties
                    .Where(property => property.Name == "value" && property.IsSubjectKey)
                    .Select(property => new { entity.ReportFileId, property.Value }))
                .ToListAsync();

            var subjectKeysByFileId = subjectKeys
                .GroupBy(item => item.ReportFileId)
                .ToDictionary(group => group.Key, group => group.First().Value);
            var direction = sortDirection == "desc" ? -1 : 1;
            var comparer = Comparer<int>.Create((leftId, rightId) =>
            {
                subjectKeysByFileId.TryGetValue(leftId, out var left);
                subjectKeysByFileId.TryGetValue(rightId, out var right);

                var comparison = CompareSubjectKeys(left, right, direction);
                return comparison != 0 ? comparison : leftId.CompareTo(rightId);
            });

            return [.. fileIds.OrderBy(id => id, comparer)];
        }

        private static int CompareSubjectKeys(string? left, string? right, int direction)
        {
            var leftMissing = string.IsNullOrWhiteSpace(left);
            var rightMissing = string.IsNullOrWhiteSpace(right);

            if (leftMissing || rightMissing)
                return leftMissing == rightMissing ? 0 : leftMissing ? 1 : -1;

            var leftNumber = TryParseDouble(left);
            var rightNumber = TryParseDouble(right);
            var comparison = leftNumber.HasValue && rightNumber.HasValue
                ? leftNumber.Value.CompareTo(rightNumber.Value)
                : StringComparer.OrdinalIgnoreCase.Compare(left, right);

            return comparison * direction;
        }

        /// <summary>
        /// Generates a hash-based cache key from the actual filtered file IDs.
        /// This ensures the cache key changes whenever the filtered dataset changes.
        /// </summary>
        private static string GenerateStatsCacheKey(List<int> allFilteredFileIds)
        {
            // Create a string representation of the filtered file IDs
            var fileIdsString = string.Join(",", allFilteredFileIds.OrderBy(x => x));

            // Hash to create a stable, short cache key
            var hash = ComputeSha256Hash(fileIdsString);

            return $"{StatsCacheKeyPrefix}{hash}";
        }

        /// <summary>
        /// Computes SHA256 hash of a string to create a stable cache key.
        /// </summary>
        private static string ComputeSha256Hash(string input)
        {
            var hashedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hashedBytes).Replace("/", "_").Replace("+", "-");
        }

        /// <summary>
        /// Fetches and calculates column statistics for the filtered dataset.
        /// Uses caching to avoid redundant calculations for the same filtered data.
        /// Cache key is based on the actual filtered file IDs, so it changes when filters change.
        /// </summary>
        private async Task<Dictionary<string, TypePivotColumnStatistics>> GetColumnStatisticsAsync(
            List<int> allFilteredFileIds,
            HashSet<string> visibleHeaderSet)
        {
            // Generate cache key based on actual filtered data
            string cacheKey = GenerateStatsCacheKey(allFilteredFileIds);

            // Try to get from cache
            if (cache.TryGetValue(cacheKey, out Dictionary<string, TypePivotColumnStatistics>? cachedStats))
            {
                return cachedStats ?? [];
            }

            // Not in cache - fetch and calculate statistics
            var statsQueryPairs = allFilteredFileIds.Count == 0 ? [] : await db.ReportEntities
                .AsNoTracking()
                .Where(e => allFilteredFileIds.Contains(e.ReportFileId))
                .SelectMany(e => e.Properties
                    .Where(p => p.Name == "value" && !p.IsSubjectKey)
                    .Select(p => new PivotPair
                    {
                        FileId = e.ReportFileId,
                        Key = e.Key,
                        Value = p.Value,
                        IsSubjectKey = false,
                        DataType = p.DataType,
                        Unit = p.Unit
                    }))
                .ToListAsync();

            // Calculate statistics for visible headers
            var columnStatistics = statsQueryPairs
                .Where(p => visibleHeaderSet.Contains(p.Key))
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => CalculateColumnStatistics(g.Select(p => p.Value)),
                    StringComparer.OrdinalIgnoreCase);

            // Store in cache with 10-minute expiration
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(StatsCacheDurationMinutes)
            };

            cache.Set(cacheKey, columnStatistics, cacheOptions);

            return columnStatistics;
        }

    }
}
