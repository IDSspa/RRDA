using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Core.Exporting;
using RRDA.Data;
using RRDA.Web.Security;
using RRDA.Web.Services.TypePivot;
using System.Globalization;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AnyUser)]
    public class TabularController(
        RRDADbContext db,
        IConfiguration configuration,
        ITypePivotPlotService typePivotPlotService,
        ITypePivotViewModelBuilder typePivotViewModelBuilder,
        ITypePivotExportService typePivotExportService,
        ITypePivotDatasetService typePivotDatasetService,
        ITypePivotStatisticsService typePivotStatisticsService) : Controller
    {
        private const string DecimalPlacesCookieName = "RRDA_TypePivot_DecimalPlaces";
        private const string RangeDisplayModeCookieName = "RRDA_TypePivot_RangeDisplayMode";
        private const int DefaultDecimalPlaces = 4;
        private const int MaxDecimalPlaces = 15;

        public async Task<IActionResult> FilePivot(int fileId)
        {
            var file = await db.ReportFiles
                .Include(f => f.ReportType)
                .Include(f => f.ReportBatch)
                .FirstOrDefaultAsync(f => f.Id == fileId);

            if (file is null) return NotFound();

            var useCompactRanges = ResolveCompactRangeDisplay();
            var dataset = await typePivotDatasetService.GetAsync(
                CreateFilterRequest(file.ReportTypeId, null, null, null, null, null, null, null, null));
            if (dataset is null) return NotFound();

            var pairs = await db.ReportEntities
                .AsNoTracking()
                .Where(e => e.ReportFileId == fileId)
                .SelectMany(e => e.Properties
                    .Where(p => p.Name == "value")
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
                .ToListAsync();

            var row = new TypePivotRow
            {
                FileId = file.Id,
                FileName = file.FileName,
                LastModified = file.FileLastModify,
                BatchId = file.ReportBatchId,
                BatchName = file.ReportBatch?.Name
            };
            foreach (var pair in pairs)
            {
                if (pair.IsSubjectKey)
                    row.SubjectKey = pair.Value;
                else if (!pair.IsRange || !useCompactRanges)
                    row.Values[pair.Key] = pair.Value;
            }
            if (useCompactRanges)
            {
                foreach (var rangeGroup in pairs
                    .Where(pair => pair.IsRange && !pair.IsSubjectKey && !string.IsNullOrWhiteSpace(pair.RangeName))
                    .GroupBy(pair => pair.RangeName!, StringComparer.OrdinalIgnoreCase))
                {
                    var cell = TypePivotRangeAggregator.Build(
                        rangeGroup.Key,
                        rangeGroup.Select(pair => pair.Unit).FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit)),
                        rangeGroup);
                    if (cell is not null)
                        row.Ranges[rangeGroup.Key] = cell;
                }
            }

            var fileRangePairs = pairs
                .Where(pair => pair.IsRange && !pair.IsSubjectKey)
                .ToList();
            var fileRangeHeaders = TypePivotRangeAggregator.BuildDescriptors(fileRangePairs);
            var headerUnits = new Dictionary<string, string?>(
                dataset.Metadata.HeaderUnits,
                StringComparer.OrdinalIgnoreCase);
            foreach (var pair in fileRangePairs.Where(pair => !string.IsNullOrWhiteSpace(pair.Unit)))
                headerUnits[pair.Key] = pair.Unit;

            var model = new FilePivotViewModel
            {
                FileId = file.Id,
                ReportTypeId = file.ReportTypeId,
                FileName = file.FileName,
                ReportTypeKey = file.ReportType?.Key ?? string.Empty,
                Headers = useCompactRanges
                    ? dataset.Metadata.VisibleHeaders
                    : [.. dataset.Metadata.VisibleHeaders, .. fileRangeHeaders.SelectMany(range => range.ExpandedHeaders)],
                RangeHeaders = useCompactRanges ? fileRangeHeaders : [],
                HeaderUnits = headerUnits,
                ReferenceHeaders = dataset.Metadata.ReferenceHeaders,
                HasSubjectKey = dataset.Metadata.HasSubjectKey,
                SubjectKeyLabel = dataset.Metadata.SubjectKeyLabel,
                Row = row,
                DecimalPlaces = ResolveDecimalPlaces()
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
            var model = await typePivotViewModelBuilder.BuildAsync(
                new TypePivotViewRequest(
                    CreateFilterRequest(
                        reportTypeId,
                        batchId,
                        lastModifiedFrom,
                        lastModifiedTo,
                        filterField,
                        filterFrom,
                        filterTo,
                        subjectKeyFrom,
                        subjectKeyTo),
                    sortField,
                    sortDirection,
                    page,
                    pageSize,
                    ResolveDecimalPlaces(),
                    ResolveCompactRangeDisplay()),
                cancellationToken);

            return model is null ? NotFound() : View(model);
        }

        /// <summary>
        /// Restituisce le statistiche aggregate (min, max, media, dev.std) per tutte le
        /// colonne numeriche visibili, calcolate sull'insieme di file corrispondente ai
        /// filtri correnti. Chiamato in modo asincrono dal client dopo il render della
        /// pagina TypePivot, solo quando l'utente espande il footer statistiche.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TypePivotColumnStats(
            int reportTypeId,
            int? batchId,
            DateTime? lastModifiedFrom,
            DateTime? lastModifiedTo,
            string? filterField,
            string? filterFrom,
            string? filterTo,
            string? subjectKeyFrom,
            string? subjectKeyTo,
            CancellationToken cancellationToken = default)
        {
            var dataset = await typePivotDatasetService.GetAsync(
                CreateFilterRequest(
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

            if (dataset is null)
                return NotFound();

            var statistics = await typePivotStatisticsService.GetAsync(
                dataset.FileIds,
                dataset.Metadata.StatisticalHeaders,
                cancellationToken);

            // Proiettiamo in un formato compatto: la view non ha bisogno di Count.
            var result = statistics.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    mean    = kv.Value.Mean,
                    min     = kv.Value.Min,
                    max     = kv.Value.Max,
                    stdDev  = kv.Value.StandardDeviation
                },
                StringComparer.OrdinalIgnoreCase);

            return Json(new { columns = result });
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
            var exportFormat = string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase)
                ? DataExportFormat.Excel
                : DataExportFormat.Csv;
            var result = await typePivotExportService.ExportAsync(
                new TypePivotExportRequest(
                    CreateFilterRequest(
                        reportTypeId,
                        batchId,
                        lastModifiedFrom,
                        lastModifiedTo,
                        filterField,
                        filterFrom,
                        filterTo,
                        subjectKeyFrom,
                        subjectKeyTo),
                    exportFormat),
                cancellationToken);

            return result is null
                ? NotFound()
                : File(result.Document.Content, result.Document.ContentType, result.FileName);
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
                    CreateFilterRequest(
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

        private static TypePivotFilterRequest CreateFilterRequest(
            int reportTypeId,
            int? batchId,
            DateTime? lastModifiedFrom,
            DateTime? lastModifiedTo,
            string? filterField,
            string? filterFrom,
            string? filterTo,
            string? subjectKeyFrom,
            string? subjectKeyTo) =>
            new(
                reportTypeId,
                batchId,
                lastModifiedFrom,
                lastModifiedTo,
                filterField,
                filterFrom,
                filterTo,
                subjectKeyFrom,
                subjectKeyTo);

        private int ResolveDecimalPlaces()
        {
            var configured = configuration.GetValue<int?>("TypePivot:DecimalPlaces");
            var fallback = Math.Clamp(configured ?? DefaultDecimalPlaces, 0, MaxDecimalPlaces);

            if (Request.Cookies.TryGetValue(DecimalPlacesCookieName, out var cookieValue)
                && int.TryParse(cookieValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cookiePlaces))
                return Math.Clamp(cookiePlaces, 0, MaxDecimalPlaces);

            return fallback;
        }

        private bool ResolveCompactRangeDisplay()
        {
            var configured = configuration.GetValue<string>("TypePivot:RangeDisplayMode") ?? "compact";
            var effective = Request.Cookies.TryGetValue(RangeDisplayModeCookieName, out var cookieValue)
                ? cookieValue
                : configured;
            return !string.Equals(effective, "expanded", StringComparison.OrdinalIgnoreCase);
        }
    }
}
