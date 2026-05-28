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
            // Validazione parametri di paging
            if (page < 1) 
                page = 1;

            // Limitiamo pageSize per evitare query troppo pesanti
            pageSize = pageSize switch { <= 0 => 50, > 200 => 200, _ => pageSize };

            // Recuperiamo il reportType per validare l'input e ottenere eventuali informazioni aggiuntive
            var reportType = await db.ReportTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == reportTypeId);

            // Se il reportType non esiste, restituiamo 404
            if (reportType is null) 
                return NotFound();

            // Costruiamo la query base per recuperare i file del reportType, applicando i filtri di batch e data se presenti
            var filesQuery = db.ReportFiles
                .AsNoTracking()
                .Where(f => f.ReportTypeId == reportTypeId);

            // Applichiamo i filtri di batch e data se sono stati specificati
            if (batchId.HasValue)
                filesQuery = filesQuery.Where(f => f.ReportBatchId == batchId.Value);

            if (uploadedFrom.HasValue)
                filesQuery = filesQuery.Where(f => f.UploadedAt >= uploadedFrom.Value);

            if (uploadedTo.HasValue)
                filesQuery = filesQuery.Where(f => f.UploadedAt <= uploadedTo.Value);

            // Ordiniamo i file per data di upload decrescente (i più recenti prima)
            filesQuery = filesQuery.OrderByDescending(f => f.UploadedAt);

            var totalFiles = await filesQuery.CountAsync();

            // Recuperiamo la pagina di file richiesta, proiettando solo i campi necessari per costruire la vista
            var filesPage = await filesQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new { f.Id, f.FileName, f.UploadedAt, f.ReportBatchId })
                .ToListAsync();

            //  Recuperiamo tutte le coppie chiave-valore per i file della pagina, in modo da costruire la matrice pivot
            var pageFileIds = filesPage.Select(f => f.Id).ToList();

            // Se non ci sono file nella pagina, evitiamo di fare la query sulle entità e restituiamo subito un modello vuoto
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

            // Otteniamo l'elenco completo di header (chiavi) presenti nelle coppie chiave-valore,
            // in modo da costruire le colonne della matrice pivot
            var headers = pairs
                .Select(p => p.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Costruiamo un dizionario di righe della matrice pivot, indicizzate per fileId,
            // inizializzando i campi fissi (FileId, FileName, UploadedAt, BatchId)
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

            // Popoliamo i valori dinamici delle righe della matrice pivot, utilizzando
            // le coppie chiave-valore recuperate
            foreach (var pair in pairs)
                rows[pair.FileId].Values[pair.Key] = pair.Value;

            // Se sono stati specificati i parametri di filtro dinamico, applichiamo il filtro alle coppie chiave-valore
            if (!string.IsNullOrWhiteSpace(filterField) && !string.IsNullOrWhiteSpace(filterValue))
            {
                pairs = [.. pairs
                    .Where(p => string.Equals(p.Key, filterField, StringComparison.OrdinalIgnoreCase)
                        && (p.Value ?? string.Empty).Contains(filterValue, StringComparison.OrdinalIgnoreCase))];

                var allowedFileIds = pairs.Select(p => p.FileId).Distinct().ToHashSet();
                rows = rows
                    .Where(kv => allowedFileIds.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            // Dopo aver applicato il filtro dinamico, ricaviamo nuovamente l'elenco di header (chiavi) presenti
            // nelle coppie chiave-valore filtrate,
            var dynamicFilterFields = pairs
                            .Select(p => p.Key)
                            .Where(k => !string.IsNullOrWhiteSpace(k))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                            .ToList();

            // Recuperiamo l'eventuale header chiave soggetto (es. "name") per il reportType, in modo da
            // evidenziarlo nella matrice pivot
            var subjectKeyHeader = await db.ReportEntities
                .AsNoTracking()
                .Where(e => pageFileIds.Contains(e.ReportFileId))
                .SelectMany(e => e.Properties
                    .Where(p => p.IsSubjectKey)
                    .Select(p => new { EntityKey = e.Key, PropertyName = p.Name, PropertyValue = p.Value }))
                .Select(x =>
                    x.PropertyName == "name" && !string.IsNullOrWhiteSpace(x.PropertyValue)
                        ? x.PropertyValue!
                        : x.EntityKey)
                .FirstOrDefaultAsync(k => !string.IsNullOrWhiteSpace(k));

            // Infine, costruiamo la lista di header visibili nella matrice pivot, applicando le seguenti regole:
            var visibleHeaders = headers
                .Where(h =>
                {
                    var vals = pairs.Where(p => string.Equals(p.Key, h, StringComparison.OrdinalIgnoreCase))
                                    .Select(p => p.Value)
                                    .Where(v => !string.IsNullOrWhiteSpace(v))
                                    .ToList();

                    if (vals.Count == 0) return false;

                    // Mostriamo solo gli header per cui TUTTI i valori non vuoti sono numerici,
                    // in modo da evitare di mostrare colonne con dati testuali non significativi
                    return vals.All(v => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                                            || double.TryParse(v, NumberStyles.Any, CultureInfo.CurrentCulture, out _));
                })
                .ToList();

            // Se l'header chiave soggetto è presente tra gli header visibili, lo spostiamo come prima colonna della matrice pivot,
            if (!string.IsNullOrWhiteSpace(subjectKeyHeader))
            {
                if (!visibleHeaders.Any(h => string.Equals(h, subjectKeyHeader, StringComparison.OrdinalIgnoreCase)))
                {
                    var subjectKeyFromHeaders = headers
                        .FirstOrDefault(h =>
                            string.Equals(h, subjectKeyHeader, StringComparison.OrdinalIgnoreCase)
                            || h.StartsWith(subjectKeyHeader + "[", StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(subjectKeyFromHeaders))
                        visibleHeaders.Add(subjectKeyFromHeaders);
                }

                visibleHeaders = [.. visibleHeaders
                    .OrderBy(h => string.Equals(h, subjectKeyHeader, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(h => h, StringComparer.OrdinalIgnoreCase)];
            }

            // Costruiamo il modello da passare alla vista, includendo tutte le informazioni necessarie per la visualizzazione
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
                Rows = [.. rows.Values.OrderByDescending(r => r.UploadedAt)],
                TotalFiles = totalFiles,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalFiles / (double)pageSize),
                DecimalPlaces = ResolveDecimalPlaces()
            };

            return View(model);
        }

        private int ResolveDecimalPlaces()
        {
            var configured = configuration.GetValue<int?>("TypePivot:DecimalPlaces");
            var fallback = Math.Clamp(configured ?? DefaultDecimalPlaces, 0, MaxDecimalPlaces);

            if (Request.Cookies.TryGetValue(DecimalPlacesCookieName, out var cookieValue)
                && int.TryParse(cookieValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cookiePlaces))
            {
                return Math.Clamp(cookiePlaces, 0, MaxDecimalPlaces);
            }

            return fallback;
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
        public int DecimalPlaces { get; set; }
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