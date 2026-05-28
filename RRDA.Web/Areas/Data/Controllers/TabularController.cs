using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;
using System.Globalization;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public class TabularController(RRDADbContext db, IConfiguration configuration) : Controller
    {
        private const string DecimalPlacesCookieName = "RRDA_TypePivot_DecimalPlaces";
        private const int DefaultDecimalPlaces = 4;
        private const int MaxDecimalPlaces = 15;

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
            DateTime? uploadedFrom,
            DateTime? uploadedTo,
            string? filterField,
            string? filterValue,
            int page = 1,
            int pageSize = 50)
        {
            // Validazione parametri di paging
            if (page < 1) 
                page = 1;

            // Limiti di pageSize: default 50, max 200
            pageSize = pageSize switch { <= 0 => 50, > 200 => 200, _ => pageSize };

            // Recupero ReportType e validazione esistenza
            var reportType = await db.ReportTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == reportTypeId);

            // Se il ReportType non esiste, restituisco 404
            if (reportType is null)
                return NotFound();

            // Query base: tutti i file del ReportType con i filtri di batch e data
            var filesQuery = db.ReportFiles
                .AsNoTracking()
                .Where(f => f.ReportTypeId == reportTypeId);

            // Applicazione filtri di batch e data
            if (batchId.HasValue)
                filesQuery = filesQuery.Where(f => f.ReportBatchId == batchId.Value);
            if (uploadedFrom.HasValue)
                filesQuery = filesQuery.Where(f => f.UploadedAt >= uploadedFrom.Value);
            if (uploadedTo.HasValue)
                filesQuery = filesQuery.Where(f => f.UploadedAt <= uploadedTo.Value);

            // Ordinamento: per data di upload decrescente
            filesQuery = filesQuery.OrderByDescending(f => f.UploadedAt);

            var totalFiles = await filesQuery.CountAsync();

            var filesPage = await filesQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new { f.Id, f.FileName, f.UploadedAt, f.ReportBatchId })
                .ToListAsync();

            var pageFileIds = filesPage.Select(f => f.Id).ToList();

            if (pageFileIds.Count == 0)
            {
                return View(new TypePivotViewModel
                {
                    ReportTypeId = reportType.Id,
                    ReportTypeKey = reportType.Key,
                    TotalFiles = totalFiles,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalFiles / (double)pageSize),
                    DecimalPlaces = ResolveDecimalPlaces()
                });
            }

            // Query unica: recupera sia SubjectKey sia le misure proiettando IsSubjectKey
            var allPairs = await db.ReportEntities
                .AsNoTracking()
                .Where(e => pageFileIds.Contains(e.ReportFileId))
                .SelectMany(e => e.Properties
                    .Where(p => p.Name == "value")
                    .Select(p => new PivotPair
                    {
                        FileId       = e.ReportFileId,
                        Key          = e.Key,
                        Value        = p.Value,
                        IsSubjectKey = p.IsSubjectKey,
                        DataType     = p.DataType,
                        Unit         = p.Unit
                    }))
                .ToListAsync();

            // Separazione SubjectKey / misure
            var subjectKeyPairs = allPairs.Where(p => p.IsSubjectKey).ToList();
            var measurePairs    = allPairs.Where(p => !p.IsSubjectKey).ToList();

            // Header di misura per il selettore del filtro dinamico
            var allMeasureHeaders = measurePairs
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Filtro dinamico su campo misura
            if (!string.IsNullOrWhiteSpace(filterField) && !string.IsNullOrWhiteSpace(filterValue))
            {
                var allowedFileIds = measurePairs
                    .Where(p => string.Equals(p.Key, filterField, StringComparison.OrdinalIgnoreCase)
                             && (p.Value ?? string.Empty).Contains(filterValue, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.FileId)
                    .Distinct()
                    .ToHashSet();

                filesPage       = [.. filesPage.Where(f => allowedFileIds.Contains(f.Id))];
                measurePairs    = [.. measurePairs.Where(p => allowedFileIds.Contains(p.FileId))];
                subjectKeyPairs = [.. subjectKeyPairs.Where(p => allowedFileIds.Contains(p.FileId))];
            }

            // Header visibili: solo colonne con i valori numerici (dal data type)
            // per evitare di mostrare campi non significativi in un pivot tabellare
            var visibleHeaders = measurePairs
                .Where(p => IsNumericDataType(p.DataType))
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var visibleHeaderSet = visibleHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var headerUnits = measurePairs
                .Where(p => visibleHeaderSet.Contains(p.Key))
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => p.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                    StringComparer.OrdinalIgnoreCase);

            // Costruzione righe
            var rows = filesPage
                .Select(f => new TypePivotRow
                {
                    FileId     = f.Id,
                    FileName   = f.FileName,
                    UploadedAt = f.UploadedAt,
                    BatchId    = f.ReportBatchId,
                    SubjectKey = null,
                    Values     = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
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
                ReportTypeId        = reportType.Id,
                ReportTypeKey       = reportType.Key,
                Headers             = visibleHeaders,
                HeaderUnits         = headerUnits,
                DynamicFilterFields = allMeasureHeaders,
                BatchId             = batchId,
                UploadedFrom        = uploadedFrom,
                UploadedTo          = uploadedTo,
                FilterField         = filterField,
                FilterValue         = filterValue,
                Rows                = [.. rows.Values.OrderByDescending(r => r.UploadedAt)],
                TotalFiles          = totalFiles,
                CurrentPage         = page,
                PageSize            = pageSize,
                TotalPages          = (int)Math.Ceiling(totalFiles / (double)pageSize),
                DecimalPlaces       = ResolveDecimalPlaces(),
                HasSubjectKey       = subjectKeyPairs.Count > 0,
                SubjectKeyLabel     = subjectKeyPairs.FirstOrDefault()?.Key ?? "SubjectKey"
            });
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
    }
}
