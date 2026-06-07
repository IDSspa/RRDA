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
        ITypePivotExportService typePivotExportService) : Controller
    {
        private const string DecimalPlacesCookieName = "RRDA_TypePivot_DecimalPlaces";
        private const int DefaultDecimalPlaces = 4;
        private const int MaxDecimalPlaces = 15;

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
                    ResolveDecimalPlaces()),
                cancellationToken);

            return model is null ? NotFound() : View(model);
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

    }
}
