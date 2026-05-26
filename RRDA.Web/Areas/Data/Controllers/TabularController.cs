using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public class TabularController(RRDADbContext db) : Controller
    {
        // Vista debug verticale (legacy)
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

        // Vista pivot orizzontale per singolo file (legacy)
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

        // Vista pivot orizzontale per tutti i file dello stesso reportType (target principale)
        public async Task<IActionResult> TypePivot(int reportTypeId, int? batchId, DateTime? uploadedFrom, DateTime? uploadedTo, string? filterField, string? filterValue, int page = 1, int pageSize = 50)
        {
            if (page < 1) page = 1;
            pageSize = pageSize switch { <= 0 => 50, > 200 => 200, _ => pageSize };

            var reportType = await db.ReportTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == reportTypeId);
            if (reportType is null) return NotFound();

            var filesQuery = db.ReportFiles
                .AsNoTracking()
                .Where(f => f.ReportTypeId == reportTypeId);

            if (batchId.HasValue)
                filesQuery = filesQuery.Where(f => f.ReportBatchId == batchId.Value);

            if (uploadedFrom.HasValue)
                filesQuery = filesQuery.Where(f => f.UploadedAt >= uploadedFrom.Value);

            if (uploadedTo.HasValue)
                filesQuery = filesQuery.Where(f => f.UploadedAt <= uploadedTo.Value);

            filesQuery = filesQuery.OrderByDescending(f => f.UploadedAt);

            var totalFiles = await filesQuery.CountAsync();

            var filesPage = await filesQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new { f.Id, f.FileName, f.UploadedAt, f.ReportBatchId })
                .ToListAsync();

            var pageFileIds = filesPage.Select(f => f.Id).ToList();

            var pairs = pageFileIds.Count == 0
                ? []
                : await db.ReportEntities
                    .AsNoTracking()
                    .Where(e => pageFileIds.Contains(e.ReportFileId))
                    .SelectMany(e => e.Properties
                        .Where(p => p.Name == "value")
                        .Select(p => new PivotPair
                        {
                            FileId = e.ReportFileId,
                            Key = e.Key,
                            Value = p.Value
                        }))
                    .ToListAsync();

            var headers = pairs
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rows = filesPage
                .Select(file => new TypePivotRow
                {
                    FileId = file.Id,
                    FileName = file.FileName,
                    UploadedAt = file.UploadedAt,
                    BatchId = file.ReportBatchId,
                    Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                })
                .ToDictionary(r => r.FileId);

            foreach (var pair in pairs)
                rows[pair.FileId].Values[pair.Key] = pair.Value;


            if (!string.IsNullOrWhiteSpace(filterField) && !string.IsNullOrWhiteSpace(filterValue))
            {
                pairs = pairs
                    .Where(p => string.Equals(p.Key, filterField, StringComparison.OrdinalIgnoreCase)
                        && (p.Value ?? string.Empty).Contains(filterValue, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var allowedFileIds = pairs.Select(p => p.FileId).Distinct().ToHashSet();
                rows = rows
                    .Where(kv => allowedFileIds.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            var dynamicFilterFields = pairs
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var visibleHeaders = headers
                .Where(h =>
                {
                    var vals = pairs.Where(p => string.Equals(p.Key, h, StringComparison.OrdinalIgnoreCase))
                                    .Select(p => p.Value)
                                    .Where(v => !string.IsNullOrWhiteSpace(v))
                                    .ToList();

                    if (!vals.Any()) return false;

                    return vals.All(v => double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)
                        || double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out _));
                })
                .ToList();

            var model = new TypePivotViewModel
            {
                ReportTypeId = reportType.Id,
                ReportTypeKey = reportType.Key,
                Headers = visibleHeaders,
                DynamicFilterFields = dynamicFilterFields,
                BatchId = batchId,
                UploadedFrom = uploadedFrom,
                UploadedTo = uploadedTo,
                FilterField = filterField,
                FilterValue = filterValue,
                Rows = rows.Values.OrderByDescending(r => r.UploadedAt).ToList(),
                TotalFiles = totalFiles,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalFiles / (double)pageSize)
            };

            return View(model);
        }
    }
    public class TabularPreviewRow
    {
        public int EntityId { get; set; }
        public string EntityKey { get; set; } = string.Empty;
        public string ReportSheet { get; set; } = string.Empty;
        public int PropertiesCount { get; set; }
    }
    public class FilePivotViewModel
    {
        public int FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ReportTypeKey { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = [];
        public Dictionary<string, string?> Row { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    public class TypePivotViewModel
    {
        public int ReportTypeId { get; set; }
        public string ReportTypeKey { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = [];
        public List<TypePivotRow> Rows { get; set; } = [];
        public List<string> DynamicFilterFields { get; set; } = [];
        public int? BatchId { get; set; }
        public DateTime? UploadedFrom { get; set; }
        public DateTime? UploadedTo { get; set; }
        public string? FilterField { get; set; }
        public string? FilterValue { get; set; }
        public int TotalFiles { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
    public class TypePivotRow
    {
        public int FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public int? BatchId { get; set; }
        public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    public class PivotPair
    {
        public int FileId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
    }
}
