using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RRDA.Core.Exporting;
using RRDA.Data;
using RRDA.Web.Security;
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
        IDataExportService exportService) : Controller
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
            int pageSize = 50)
        {
            // Validazione parametri di paging
            if (page < 1)
                page = 1;

            // Limiti di pageSize: default 50, max 200
            pageSize = pageSize switch { <= 0 => 50, > 200 => 200, _ => pageSize };

            var filterResult = await GetTypePivotFilterResultAsync(
                reportTypeId,
                batchId,
                lastModifiedFrom,
                lastModifiedTo,
                filterField,
                filterFrom,
                filterTo,
                subjectKeyFrom,
                subjectKeyTo);

            if (filterResult is null)
                return NotFound();

            var reportType = filterResult.ReportType;
            var allFilteredFileIds = filterResult.AllFilteredFileIds;
            var metadata = await GetTypePivotMetadataAsync(allFilteredFileIds);
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
            string? format)
        {
            var filterResult = await GetTypePivotFilterResultAsync(
                reportTypeId,
                batchId,
                lastModifiedFrom,
                lastModifiedTo,
                filterField,
                filterFrom,
                filterTo,
                subjectKeyFrom,
                subjectKeyTo);

            if (filterResult is null)
                return NotFound();

            var fileIds = filterResult.AllFilteredFileIds;
            var metadata = await GetTypePivotMetadataAsync(fileIds);
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
            [FromQuery] int[] selectedFileIds)
        {
            var filterResult = await GetTypePivotFilterResultAsync(
                reportTypeId,
                batchId,
                lastModifiedFrom,
                lastModifiedTo,
                filterField,
                filterFrom,
                filterTo,
                subjectKeyFrom,
                subjectKeyTo);

            if (filterResult is null)
                return NotFound();

            var metadata = await GetTypePivotMetadataAsync(filterResult.AllFilteredFileIds);
            var allowedSeriesFields = metadata.VisibleHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedSeriesFields = seriesFields
                .Where(f => !string.IsNullOrWhiteSpace(f) && allowedSeriesFields.Contains(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var yAxisTitle = GetPlotYAxisTitle(selectedSeriesFields, metadata.HeaderUnits);

            if (selectedSeriesFields.Count == 0)
                return BadRequest(new
                {
                    message = "Selezionare almeno una grandezza numerica valida.",
                    availableSeriesFields = metadata.VisibleHeaders
                });

            var normalizedChartType = string.Equals(chartType, "pdf", StringComparison.OrdinalIgnoreCase)
                ? "pdf"
                : "scatter";

            if (normalizedChartType == "pdf")
            {
                return await BuildTypePivotPdfPlotDataAsync(
                    filterResult,
                    metadata,
                    selectedSeriesFields,
                    selectedFileIds);
            }

            var selectedFileIdSet = selectedFileIds.ToHashSet();
            var plotFileIds = selectedFileIdSet.Count == 0
                ? filterResult.AllFilteredFileIds
                : [.. filterResult.AllFilteredFileIds.Where(selectedFileIdSet.Contains)];

            var files = plotFileIds.Count == 0 ? [] : await db.ReportFiles
                .AsNoTracking()
                .Where(f => plotFileIds.Contains(f.Id))
                .Select(f => new { f.Id, f.FileName, f.FileLastModify, f.ReportBatchId })
                .ToListAsync();

            var fileOrder = filterResult.AllFilteredFileIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index);

            files = [.. files.OrderBy(f => fileOrder.TryGetValue(f.Id, out var index) ? index : int.MaxValue)];
            var fileIds = files.Select(f => f.Id).ToList();

            var pairs = fileIds.Count == 0 ? [] : await db.ReportEntities
                .AsNoTracking()
                .Where(e => fileIds.Contains(e.ReportFileId))
                .SelectMany(e => e.Properties
                    .Where(p => p.Name == "value" && (p.IsSubjectKey || selectedSeriesFields.Contains(e.Key)))
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

            var subjectKeysByFileId = pairs
                .Where(p => p.IsSubjectKey)
                .GroupBy(p => p.FileId)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Value);

            var measureValuesByFileId = pairs
                .Where(p => !p.IsSubjectKey)
                .GroupBy(p => p.FileId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            keyGroup => keyGroup.Key,
                            keyGroup => keyGroup.LastOrDefault()?.Value,
                            StringComparer.OrdinalIgnoreCase));

            var resolvedXField = ResolvePlotXAxisField(xField, metadata.HasSubjectKey);

            var markerSymbols = new[]
            {
                "circle",
                "square",
                "diamond",
                "cross",
                "x",
                "triangle-up",
                "triangle-down",
                "star"
            };

            var traces = selectedSeriesFields.Select((field, index) =>
            {
                var xValues = new List<object?>();
                var yValues = new List<double?>();
                var subjectKeyValues = new List<object?>();

                foreach (var file in files)
                {
                    if (!measureValuesByFileId.TryGetValue(file.Id, out var values)
                        || !values.TryGetValue(field, out var rawY))
                        continue;

                    var y = TryParseDouble(rawY);
                    if (!y.HasValue)
                        continue;

                    xValues.Add(GetPlotXAxisValue(
                        resolvedXField,
                        file.Id,
                        file.FileName,
                        file.FileLastModify,
                        file.ReportBatchId,
                        filterResult.BatchNames,
                        subjectKeysByFileId));
                    yValues.Add(y.Value);
                    subjectKeyValues.Add(subjectKeysByFileId.TryGetValue(file.Id, out var subjectKey)
                        ? FormatSubjectKeyValue(subjectKey)
                        : null);
                }

                return new
                {
                    name = FormatHeaderLabel(field, metadata.HeaderUnits),
                    type = "scatter",
                    mode = "markers",
                    marker = new
                    {
                        symbol = markerSymbols[index % markerSymbols.Length],
                        size = 8
                    },
                    x = xValues,
                    y = yValues,
                    customdata = subjectKeyValues,
                    hovertemplate = $"{metadata.SubjectKeyLabel}: %{{customdata}}<br>X: %{{x}}<br>Y: %{{y}}<extra>%{{fullData.name}}</extra>"
                };
            }).ToList();

            return Json(new
            {
                reportTypeId = filterResult.ReportType.Id,
                reportTypeKey = filterResult.ReportType.Key,
                rowCount = files.Count,
                selectedFileIds = fileIds,
                availableXFields = metadata.PlotXAxisFields,
                availableSeriesFields = metadata.VisibleHeaders,
                layout = new
                {
                    title = $"Scatter - {filterResult.ReportType.Key}",
                    xaxis = new { title = new { text = GetPlotXAxisTitle(resolvedXField, metadata.SubjectKeyLabel) } },
                    yaxis = new { title = new { text = yAxisTitle } },
                    hovermode = "closest"
                },
                traces
            });
        }

        private async Task<IActionResult> BuildTypePivotPdfPlotDataAsync(
            TypePivotFilterResult filterResult,
            TypePivotMetadata metadata,
            List<string> selectedSeriesFields,
            int[] selectedFileIds)
        {
            var fileIds = filterResult.AllFilteredFileIds;
            var pairs = fileIds.Count == 0 ? [] : await db.ReportEntities
                .AsNoTracking()
                .Where(e => fileIds.Contains(e.ReportFileId))
                .SelectMany(e => e.Properties
                    .Where(p => p.Name == "value" && (p.IsSubjectKey || selectedSeriesFields.Contains(e.Key)))
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

            var subjectKeysByFileId = pairs
                .Where(p => p.IsSubjectKey)
                .GroupBy(p => p.FileId)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Value);

            var selectedFileIdSet = selectedFileIds.ToHashSet();
            var colors = new[]
            {
                "#1f77b4",
                "#d62728",
                "#2ca02c",
                "#ff7f0e",
                "#9467bd",
                "#17becf",
                "#8c564b",
                "#e377c2"
            };
            var traces = new List<object>();
            var skippedFields = new List<string>();
            var maxDensity = 0d;

            foreach (var field in selectedSeriesFields)
            {
                var seriesIndex = selectedSeriesFields.IndexOf(field);
                var color = colors[seriesIndex % colors.Length];
                var valuesByFileId = pairs
                    .Where(p => !p.IsSubjectKey && string.Equals(p.Key, field, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(p => p.FileId)
                    .Select(g => new
                    {
                        FileId = g.Key,
                        Value = TryParseDouble(g.LastOrDefault()?.Value)
                    })
                    .Where(x => x.Value.HasValue)
                    .Select(x => new PdfSample { FileId = x.FileId, Value = x.Value!.Value })
                    .OrderBy(x => x.Value)
                    .ToList();

                if (valuesByFileId.Count < 2)
                {
                    skippedFields.Add(field);
                    continue;
                }

                var values = valuesByFileId.Select(x => x.Value).ToList();
                var mean = values.Average();
                var variance = values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1);
                var stdDev = Math.Sqrt(variance);
                if (stdDev <= 0)
                {
                    skippedFields.Add(field);
                    continue;
                }

                var min = values.Min();
                var max = values.Max();
                var binWidth = stdDev / 2;
                var binStart = mean - Math.Ceiling((mean - min) / binWidth) * binWidth;
                var binEnd = mean + Math.Ceiling((max - mean) / binWidth) * binWidth;
                var curveStart = Math.Min(min, mean - 4 * stdDev);
                var curveEnd = Math.Max(max, mean + 4 * stdDev);
                var curveStep = (curveEnd - curveStart) / 160;
                if (curveStep <= 0)
                    curveStep = stdDev / 20;

                var curveX = new List<double>();
                var curveY = new List<double>();
                for (var x = curveStart; x <= curveEnd; x += curveStep)
                {
                    curveX.Add(x);
                    curveY.Add(NormalPdf(x, mean, stdDev));
                }

                var histogramBins = BuildPdfHistogramBins(
                    valuesByFileId,
                    subjectKeysByFileId,
                    metadata.SubjectKeyLabel,
                    binStart,
                    binEnd + binWidth,
                    binWidth);
                var curveMax = curveY.Count == 0 ? 0 : curveY.Max();
                var histMax = histogramBins.Count == 0 ? 0 : histogramBins.Max(b => b.Density);
                var seriesMaxDensity = Math.Max(curveMax, histMax);
                maxDensity = Math.Max(maxDensity, seriesMaxDensity);
                var displayName = FormatHeaderLabel(field, metadata.HeaderUnits);

                traces.Add(new
                {
                    name = $"{displayName} istogramma",
                    type = "bar",
                    opacity = 0.45,
                    x = histogramBins.Select(b => b.Center).ToList(),
                    y = histogramBins.Select(b => b.Density).ToList(),
                    width = histogramBins.Select(_ => binWidth).ToList(),
                    customdata = histogramBins
                        .Select(b => new object[] { b.RangeLabel, b.Count, b.SubjectKeysSummary })
                        .ToList(),
                    marker = new
                    {
                        color = ToRgba(color, 0.55),
                        line = new { color, width = 1 }
                    },
                    hovertemplate = $"Intervallo: %{{customdata[0]}}<br>Campioni: %{{customdata[1]}}<br>{metadata.SubjectKeyLabel}: %{{customdata[2]}}<br>Densita: %{{y}}<extra>%{{fullData.name}}</extra>"
                });

                traces.Add(new
                {
                    name = $"{displayName} gaussiana",
                    type = "scatter",
                    mode = "lines",
                    x = curveX,
                    y = curveY,
                    line = new { color, width = 2 },
                    hovertemplate = "Valore: %{x}<br>Densita: %{y}<extra>%{fullData.name}</extra>"
                });

                var lineHeight = seriesMaxDensity * 1.1;
                traces.Add(new
                {
                    name = $"{displayName} media",
                    type = "scatter",
                    mode = "lines",
                    x = new[] { mean, mean },
                    y = new[] { 0, lineHeight },
                    line = new { color, width = 3 },
                    showlegend = false,
                    hovertemplate = $"Media: {mean.ToString("G6", CultureInfo.InvariantCulture)}<extra>{displayName}</extra>"
                });

                foreach (var sigmaValue in new[] { mean - stdDev, mean + stdDev })
                {
                    traces.Add(new
                    {
                        name = $"{displayName} sigma",
                        type = "scatter",
                        mode = "lines",
                        x = new[] { sigmaValue, sigmaValue },
                        y = new[] { 0, lineHeight },
                        line = new { color, width = 2, dash = "dash" },
                        showlegend = false,
                        hovertemplate = $"Sigma: {sigmaValue.ToString("G6", CultureInfo.InvariantCulture)}<extra>{displayName}</extra>"
                    });
                }

                var selectedValues = valuesByFileId
                    .Where(x => selectedFileIdSet.Contains(x.FileId))
                    .Select(x => new
                    {
                        x.FileId,
                        x.Value,
                        Density = NormalPdf(x.Value, mean, stdDev),
                        SubjectKey = subjectKeysByFileId.TryGetValue(x.FileId, out var subjectKey)
                            ? FormatSubjectKeyValue(subjectKey)
                            : null
                    })
                    .ToList();

                if (selectedValues.Count > 0)
                {
                    traces.Add(new
                    {
                        name = $"{displayName} selezionati",
                        type = "scatter",
                        mode = "markers",
                        x = selectedValues.Select(x => x.Value).ToList(),
                        y = selectedValues.Select(x => x.Density).ToList(),
                        customdata = selectedValues.Select(x => x.SubjectKey).ToList(),
                        marker = new
                        {
                            color,
                            size = 10,
                            symbol = "circle",
                            line = new { color = "#000000", width = 1 }
                        },
                        hovertemplate = $"{metadata.SubjectKeyLabel}: %{{customdata}}<br>Valore: %{{x}}<br>Densita: %{{y}}<extra>%{{fullData.name}}</extra>"
                    });
                }
            }

            if (traces.Count == 0)
                return BadRequest(new
                {
                    message = "Non ci sono abbastanza campioni numerici per generare la densita di probabilita.",
                    skippedFields
                });

            var xAxisTitle = GetPlotYAxisTitle(selectedSeriesFields, metadata.HeaderUnits);

            return Json(new
            {
                reportTypeId = filterResult.ReportType.Id,
                reportTypeKey = filterResult.ReportType.Key,
                chartType = "pdf",
                rowCount = fileIds.Count,
                skippedFields,
                layout = new
                {
                    title = $"PDF - {filterResult.ReportType.Key}",
                    xaxis = new { title = new { text = xAxisTitle } },
                    yaxis = new { title = new { text = "Probability Density Function" }, range = new[] { 0, maxDensity * 1.25 } },
                    hovermode = "closest",
                    barmode = "overlay"
                },
                traces
            });
        }

        private async Task<TypePivotFilterResult?> GetTypePivotFilterResultAsync(
            int reportTypeId,
            int? batchId,
            DateTime? lastModifiedFrom,
            DateTime? lastModifiedTo,
            string? filterField,
            string? filterFrom,
            string? filterTo,
            string? subjectKeyFrom,
            string? subjectKeyTo)
        {
            var reportType = await db.ReportTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == reportTypeId);

            if (reportType is null)
                return null;

            var batchRecords = await db.ReportBatches
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name, b.Description })
                .ToListAsync();

            var filesQuery = db.ReportFiles
                .AsNoTracking()
                .Where(f => f.ReportTypeId == reportTypeId);

            if (batchId.HasValue)
                filesQuery = filesQuery.Where(f => f.ReportBatchId == batchId.Value);
            if (lastModifiedFrom.HasValue)
                filesQuery = filesQuery.Where(f => f.FileLastModify >= lastModifiedFrom.Value);
            if (lastModifiedTo.HasValue)
                filesQuery = filesQuery.Where(f => f.FileLastModify <= lastModifiedTo.Value);

            var allFilteredFileIds = await filesQuery
                .OrderByDescending(f => f.FileLastModify)
                .Select(f => f.Id)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(filterField)
                && (!string.IsNullOrWhiteSpace(filterFrom) || !string.IsNullOrWhiteSpace(filterTo)))
            {
                var measurePairsForFilter = allFilteredFileIds.Count == 0 ? [] : await db.ReportEntities
                    .AsNoTracking()
                    .Where(e => allFilteredFileIds.Contains(e.ReportFileId))
                    .SelectMany(e => e.Properties
                        .Where(p => p.Name == "value" && !p.IsSubjectKey && e.Key == filterField)
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

                var allowedFileIds = measurePairsForFilter
                    .Where(p => IsValueInRange(p.Value, filterFrom, filterTo))
                    .Select(p => p.FileId)
                    .Distinct()
                    .ToHashSet();

                allFilteredFileIds = [.. allFilteredFileIds.Where(id => allowedFileIds.Contains(id))];
            }

            if (!string.IsNullOrWhiteSpace(subjectKeyFrom) || !string.IsNullOrWhiteSpace(subjectKeyTo))
            {
                var subjectKeyPairsForFilter = allFilteredFileIds.Count == 0 ? [] : await db.ReportEntities
                    .AsNoTracking()
                    .Where(e => allFilteredFileIds.Contains(e.ReportFileId))
                    .SelectMany(e => e.Properties
                        .Where(p => p.Name == "value" && p.IsSubjectKey)
                        .Select(p => new PivotPair
                        {
                            FileId = e.ReportFileId,
                            Key = e.Key,
                            Value = p.Value,
                            IsSubjectKey = true,
                            DataType = p.DataType,
                            Unit = p.Unit
                        }))
                    .ToListAsync();

                var allowedFileIds = subjectKeyPairsForFilter
                    .Where(p => IsValueInRange(p.Value, subjectKeyFrom, subjectKeyTo))
                    .Select(p => p.FileId)
                    .Distinct()
                    .ToHashSet();

                allFilteredFileIds = [.. allFilteredFileIds.Where(id => allowedFileIds.Contains(id))];
            }

            return new TypePivotFilterResult
            {
                ReportType = reportType,
                AllFilteredFileIds = allFilteredFileIds,
                BatchOptions = batchRecords
                    .Select(b => new TypePivotBatchOption
                    {
                        Id = b.Id,
                        Label = string.IsNullOrWhiteSpace(b.Description)
                            ? b.Name
                            : $"{b.Name} - {b.Description}"
                    })
                    .ToList(),
                BatchNames = batchRecords.ToDictionary(b => b.Id, b => b.Name)
            };
        }

        private async Task<TypePivotMetadata> GetTypePivotMetadataAsync(List<int> allFilteredFileIds)
        {
            var pairs = allFilteredFileIds.Count == 0 ? [] : await db.ReportEntities
                .AsNoTracking()
                .Where(e => allFilteredFileIds.Contains(e.ReportFileId))
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

            var subjectKeyPairs = pairs.Where(p => p.IsSubjectKey).ToList();
            var measurePairs = pairs.Where(p => !p.IsSubjectKey).ToList();
            var visibleHeaders = measurePairs
                .Where(p => IsNumericDataType(p.DataType))
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var visibleHeaderSet = visibleHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var plotXAxisFields = new List<string> { "FileName", "LastModified", "Batch" };
            if (subjectKeyPairs.Count > 0)
                plotXAxisFields.Insert(0, "SubjectKey");

            return new TypePivotMetadata
            {
                AllMeasureHeaders = measurePairs
                    .Select(p => p.Key)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                VisibleHeaders = visibleHeaders,
                HeaderUnits = measurePairs
                    .Where(p => visibleHeaderSet.Contains(p.Key))
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

        private static string ResolvePlotXAxisField(string? xField, bool hasSubjectKey)
        {
            if (string.Equals(xField, "FileName", StringComparison.OrdinalIgnoreCase))
                return "FileName";
            if (string.Equals(xField, "LastModified", StringComparison.OrdinalIgnoreCase))
                return "LastModified";
            if (string.Equals(xField, "Batch", StringComparison.OrdinalIgnoreCase))
                return "Batch";

            return hasSubjectKey ? "SubjectKey" : "FileName";
        }

        private static object? GetPlotXAxisValue(
            string xField,
            int fileId,
            string fileName,
            DateTime lastModified,
            int? batchId,
            Dictionary<int, string> batchNames,
            Dictionary<int, string?> subjectKeysByFileId)
        {
            return xField switch
            {
                "SubjectKey" => subjectKeysByFileId.TryGetValue(fileId, out var subjectKey)
                    ? FormatSubjectKeyValue(subjectKey)
                    : null,
                "LastModified" => lastModified.ToString("O", CultureInfo.InvariantCulture),
                "Batch" => batchId.HasValue && batchNames.TryGetValue(batchId.Value, out var batchName)
                    ? batchName
                    : null,
                _ => fileName
            };
        }

        private static object? FormatSubjectKeyValue(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return null;
            if (long.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var integerValue))
                return integerValue;
            if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
                return doubleValue;

            return rawValue;
        }

        private static string GetPlotXAxisTitle(string xField, string subjectKeyLabel)
        {
            return xField switch
            {
                "SubjectKey" => subjectKeyLabel,
                "LastModified" => "Ultima modifica",
                "Batch" => "Batch",
                _ => "Nome file"
            };
        }

        private static string FormatHeaderLabel(string header, Dictionary<string, string?> headerUnits)
        {
            return headerUnits.TryGetValue(header, out var unit) && !string.IsNullOrWhiteSpace(unit)
                ? $"{header} [{unit}]"
                : header;
        }

        private static string GetPlotYAxisTitle(
            List<string> selectedSeriesFields,
            Dictionary<string, string?> headerUnits)
        {
            var units = selectedSeriesFields
                .Select(field => headerUnits.TryGetValue(field, out var unit) ? unit : null)
                .Where(unit => !string.IsNullOrWhiteSpace(unit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedSeriesFields.Count == 1)
                return FormatHeaderLabel(selectedSeriesFields[0], headerUnits);

            return units.Count == 1
                ? $"Valore [{units[0]}]"
                : $"Valore [{string.Join(" / ", units)}]";
        }

        private static double NormalPdf(double x, double mean, double standardDeviation)
        {
            if (standardDeviation <= 0)
                return 0;

            var z = (x - mean) / standardDeviation;
            return Math.Exp(-0.5 * z * z) / (standardDeviation * Math.Sqrt(2 * Math.PI));
        }

        private static List<PdfHistogramBin> BuildPdfHistogramBins(
            List<PdfSample> samples,
            Dictionary<int, string?> subjectKeysByFileId,
            string subjectKeyLabel,
            double start,
            double end,
            double binWidth)
        {
            if (samples.Count == 0 || binWidth <= 0 || end <= start)
                return [];

            var binCount = Math.Max(1, (int)Math.Ceiling((end - start) / binWidth));
            var bins = Enumerable.Range(0, binCount)
                .Select(index => new List<PdfSample>())
                .ToList();

            foreach (var sample in samples)
            {
                var index = Math.Min(binCount - 1, Math.Max(0, (int)Math.Floor((sample.Value - start) / binWidth)));
                bins[index].Add(sample);
            }

            return bins
                .Select((binSamples, index) =>
                {
                    var binStart = start + index * binWidth;
                    var binEnd = binStart + binWidth;
                    var subjectKeys = binSamples
                        .Select(sample => subjectKeysByFileId.TryGetValue(sample.FileId, out var subjectKey)
                            ? FormatSubjectKeyValue(subjectKey)?.ToString()
                            : null)
                        .Where(subjectKey => !string.IsNullOrWhiteSpace(subjectKey))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(subjectKey => subjectKey, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new PdfHistogramBin
                    {
                        Center = binStart + binWidth / 2,
                        Density = binSamples.Count / (samples.Count * binWidth),
                        Count = binSamples.Count,
                        RangeLabel = FormattableString.Invariant($"{binStart:G6} - {binEnd:G6}"),
                        SubjectKeysSummary = FormatSubjectKeySummary(subjectKeys, subjectKeyLabel)
                    };
                })
                .ToList();
        }

        private static string FormatSubjectKeySummary(List<string?> subjectKeys, string subjectKeyLabel)
        {
            const int maxItems = 12;

            if (subjectKeys.Count == 0)
                return $"{subjectKeyLabel} non disponibile";

            var visibleItems = subjectKeys.Take(maxItems).ToList();
            var suffix = subjectKeys.Count > maxItems
                ? $" (+{subjectKeys.Count - maxItems})"
                : string.Empty;

            return $"{string.Join(", ", visibleItems)}{suffix}";
        }

        private static double CalculateHistogramDensityMax(List<double> values, double min, double max, double binWidth)
        {
            if (values.Count == 0 || binWidth <= 0)
                return 0;

            var binCount = Math.Max(1, (int)Math.Ceiling((max - min) / binWidth));
            var counts = new int[binCount];
            foreach (var value in values)
            {
                var index = Math.Min(binCount - 1, Math.Max(0, (int)Math.Floor((value - min) / binWidth)));
                counts[index]++;
            }

            return counts.Max() / (values.Count * binWidth);
        }

        private static string ToRgba(string hexColor, double alpha)
        {
            var normalized = hexColor.TrimStart('#');
            if (normalized.Length != 6)
                return hexColor;

            var red = int.Parse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var green = int.Parse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var blue = int.Parse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return FormattableString.Invariant($"rgba({red},{green},{blue},{alpha})");
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

        private static bool IsNumericDataType(string? dataType)
        {
            return string.Equals(dataType, "int", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dataType, "double", StringComparison.OrdinalIgnoreCase);
        }

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

        private sealed class TypePivotFilterResult
        {
            public required RRDA.Data.ReportType ReportType { get; init; }
            public required List<int> AllFilteredFileIds { get; init; }
            public required List<TypePivotBatchOption> BatchOptions { get; init; }
            public required Dictionary<int, string> BatchNames { get; init; }
        }

        private sealed class TypePivotMetadata
        {
            public List<string> AllMeasureHeaders { get; init; } = [];
            public List<string> VisibleHeaders { get; init; } = [];
            public Dictionary<string, string?> HeaderUnits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public bool HasSubjectKey { get; init; }
            public string SubjectKeyLabel { get; init; } = "SubjectKey";
            public List<string> PlotXAxisFields { get; init; } = [];
        }

        private sealed class PdfSample
        {
            public int FileId { get; init; }
            public double Value { get; init; }
        }

        private sealed class PdfHistogramBin
        {
            public double Center { get; init; }
            public double Density { get; init; }
            public int Count { get; init; }
            public string RangeLabel { get; init; } = string.Empty;
            public string SubjectKeysSummary { get; init; } = string.Empty;
        }
    }
}
