using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RRDA.Core.Exporting;
using RRDA.Data;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AnyUser)]
    public class ExportController(
        RRDADbContext db,
        IDataExportService exportService) : Controller
    {
        public async Task<IActionResult> Index(
            int? reportTypeId,
            DateTime? from,
            DateTime? to)
        {
            ViewBag.ReportTypes = new SelectList(
                await db.ReportTypes.OrderBy(type => type.Key).ToListAsync(),
                "Id",
                "Key",
                reportTypeId);

            ViewBag.Filters = new
            {
                reportTypeId,
                from = from?.ToString("yyyy-MM-dd"),
                to = to?.ToString("yyyy-MM-dd")
            };

            if (reportTypeId.HasValue || from.HasValue || to.HasValue)
                ViewBag.PreviewCount = await BuildQuery(reportTypeId, from, to).CountAsync();

            return View();
        }

        public async Task<IActionResult> Csv(
            int? reportTypeId,
            DateTime? from,
            DateTime? to)
        {
            var entities = await LoadEntitiesAsync(reportTypeId, from, to);
            return Export(entities, DataExportFormat.Csv);
        }

        public async Task<IActionResult> Excel(
            int? reportTypeId,
            DateTime? from,
            DateTime? to)
        {
            var entities = await LoadEntitiesAsync(reportTypeId, from, to);
            return Export(entities, DataExportFormat.Excel);
        }

        private async Task<List<ReportEntity>> LoadEntitiesAsync(
            int? reportTypeId,
            DateTime? from,
            DateTime? to)
        {
            return await BuildQuery(reportTypeId, from, to)
                .Include(entity => entity.Properties)
                .Include(entity => entity.ReportFile)
                    .ThenInclude(file => file.ReportType)
                .ToListAsync();
        }

        private IQueryable<ReportEntity> BuildQuery(
            int? reportTypeId,
            DateTime? from,
            DateTime? to)
        {
            var query = db.ReportEntities.AsQueryable();

            if (reportTypeId.HasValue)
                query = query.Where(entity => entity.ReportFile.ReportTypeId == reportTypeId.Value);

            if (from.HasValue)
                query = query.Where(entity => entity.ReportFile.UploadedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(entity => entity.ReportFile.UploadedAt <= to.Value.AddDays(1));

            return query;
        }

        private IActionResult Export(
            IReadOnlyCollection<ReportEntity> entities,
            DataExportFormat format)
        {
            var document = exportService.Export(BuildExportTable(entities), format);
            var fileName = $"RRDA_export_{DateTime.Now:yyyyMMdd_HHmmss}{document.FileExtension}";

            return File(document.Content, document.ContentType, fileName);
        }

        private static DataExportTable BuildExportTable(IReadOnlyCollection<ReportEntity> entities)
        {
            var propertyNames = entities
                .SelectMany(entity => entity.Properties.Select(property => property.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var columns = new List<DataExportColumn>
            {
                new("FileId", "FileId"),
                new("FileName", "FileName"),
                new("ReportType", "ReportType"),
                new("ImportedBy", "ImportedBy"),
                new("ImportedAt", "ImportedAt"),
                new("EntityKind", "EntityKind"),
                new("EntityKey", "EntityKey")
            };
            columns.AddRange(propertyNames.Select(name => new DataExportColumn(
                GetPropertyColumnKey(name),
                name)));

            var rows = entities.Select(entity =>
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FileId"] = entity.ReportFileId,
                    ["FileName"] = entity.ReportFile.FileName,
                    ["ReportType"] = entity.ReportFile.ReportType.Key,
                    ["ImportedBy"] = entity.ReportFile.ImportedBy,
                    ["ImportedAt"] = entity.ReportFile.UploadedAt,
                    ["EntityKind"] = entity.ReportSheet,
                    ["EntityKey"] = entity.Key
                };

                foreach (var property in entity.Properties)
                    row[GetPropertyColumnKey(property.Name)] = property.Value;

                return (IReadOnlyDictionary<string, object?>)row;
            }).ToList();

            return new DataExportTable(columns, rows);
        }

        private static string GetPropertyColumnKey(string propertyName)
            => $"Property:{propertyName}";
    }
}
