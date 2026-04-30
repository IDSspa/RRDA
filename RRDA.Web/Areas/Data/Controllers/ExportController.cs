using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;
using System.Text;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AnyUser)]
    public class ExportController(RRDADbContext db) : Controller
    {
        // ── GET /Data/Export ──────────────────────────────────────────────
        public async Task<IActionResult> Index(int? reportTypeId,
            DateTime? from, DateTime? to)
        {
            ViewBag.ReportTypes = new SelectList(
                await db.ReportTypes.OrderBy(t => t.Key).ToListAsync(),
                "Id", "Key", reportTypeId);

            ViewBag.Filters = new
            {
                reportTypeId,
                from = from?.ToString("yyyy-MM-dd"),
                to   = to?.ToString("yyyy-MM-dd")
            };

            // Conteggio anteprima
            if (reportTypeId.HasValue || from.HasValue || to.HasValue)
            {
                var q = BuildQuery(reportTypeId, from, to);
                ViewBag.PreviewCount = await q.CountAsync();
            }

            return View();
        }

        // ── GET /Data/Export/Csv ──────────────────────────────────────────
        public async Task<IActionResult> Csv(int? reportTypeId,
            DateTime? from, DateTime? to)
        {
            var entities = await BuildQuery(reportTypeId, from, to)
                .Include(e => e.Properties)
                .Include(e => e.ReportFile)
                    .ThenInclude(f => f.ReportType)
                .ToListAsync();

            // Raccoglie tutte le property keys distinte per costruire le colonne
            var allKeys = entities
                .SelectMany(e => e.Properties.Select(p => p.Name))
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            var sb = new StringBuilder();

            // Header
            var header = new List<string>
                { "FileId", "FileName", "ReportType", "ImportedBy",
                  "ImportedAt", "EntityKind", "EntityKey" };
            header.AddRange(allKeys);
            sb.AppendLine(CsvRow(header));

            // Righe
            foreach (var e in entities)
            {
                var row = new List<string>
                {
                    e.ReportFileId.ToString(),
                    e.ReportFile?.FileName  ?? "",
                    e.ReportFile?.ReportType?.Key ?? "",
                    e.ReportFile?.ImportedBy ?? "",
                    e.ReportFile?.UploadedAt.ToString("yyyy-MM-dd HH:mm") ?? "",
                    e.Key
                };

                var propDict = e.Properties.ToDictionary(p => p.Name, p => p.Value ?? "");
                foreach (var k in allKeys)
                    row.Add(propDict.TryGetValue(k, out var v) ? v : "");

                sb.AppendLine(CsvRow(row));
            }

            var bytes = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
                .ToArray();

            var fileName = $"RRDA_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv", fileName);
        }

        // ── GET /Data/Export/Excel ────────────────────────────────────────
        public async Task<IActionResult> Excel(int? reportTypeId,
            DateTime? from, DateTime? to)
        {
            // Esportiamo come CSV con estensione .xlsx per semplicità —
            // Excel apre i CSV correttamente se il BOM UTF-8 è presente.
            // Per un vero .xlsx servirebbero ClosedXML o EPPlus (non inclusi).
            var entities = await BuildQuery(reportTypeId, from, to)
                .Include(e => e.Properties)
                .Include(e => e.ReportFile)
                    .ThenInclude(f => f.ReportType)
                .ToListAsync();

            var allKeys = entities
                .SelectMany(e => e.Properties.Select(p => p.Name))
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            var sb = new StringBuilder();

            var header = new List<string>
                { "FileId", "FileName", "ReportType", "ImportedBy",
                  "ImportedAt", "EntityKind", "EntityKey" };
            header.AddRange(allKeys);
            sb.AppendLine(CsvRow(header, separator: "\t"));

            foreach (var e in entities)
            {
                var row = new List<string>
                {
                    e.ReportFileId.ToString(),
                    e.ReportFile?.FileName  ?? "",
                    e.ReportFile?.ReportType?.Key ?? "",
                    e.ReportFile?.ImportedBy ?? "",
                    e.ReportFile?.UploadedAt.ToString("yyyy-MM-dd HH:mm") ?? "",
                    e.ReportSheet,
                    e.Key
                };

                var propDict = e.Properties.ToDictionary(p => p.Name, p => p.Value ?? "");
                foreach (var k in allKeys)
                    row.Add(propDict.TryGetValue(k, out var v) ? v : "");

                sb.AppendLine(CsvRow(row, separator: "\t"));
            }

            var bytes = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
                .ToArray();

            var fileName = $"RRDA_export_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
            return File(bytes, "application/vnd.ms-excel", fileName);
        }

        // ── Helper ────────────────────────────────────────────────────────
        private IQueryable<ReportEntity> BuildQuery(
            int? reportTypeId, DateTime? from, DateTime? to)
        {
            var q = db.ReportEntities.AsQueryable();

            if (reportTypeId.HasValue)
                q = q.Where(e => e.ReportFile.ReportTypeId == reportTypeId.Value);

            if (from.HasValue)
                q = q.Where(e => e.ReportFile.UploadedAt >= from.Value);

            if (to.HasValue)
                q = q.Where(e => e.ReportFile.UploadedAt <= to.Value.AddDays(1));

            return q;
        }

        private static string CsvRow(IEnumerable<string> fields,
            string separator = ",")
            => string.Join(separator,
                fields.Select(f => $"\"{f.Replace("\"", "\"\"")}\""));
    }
}
