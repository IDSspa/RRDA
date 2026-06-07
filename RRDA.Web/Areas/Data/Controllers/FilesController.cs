using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RRDA.Core;
using RRDA.Core.Validator;
using RRDA.Data;
using RRDA.Plugins.Common;
using RRDA.Web.Areas.Data.Models;
using RRDA.Web.Security;
using RRDA.Web.Services;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AnyUser)]
    public class FilesController(
        RRDADbContext db,
        IPluginCatalog pluginCatalog,
        IImportResultRepository importResultRepository,
        IWebAuditService auditService,
        ILogger<FilesController> logger) : Controller
    {
        // ── GET /Data/Files ───────────────────────────────────────────────
        public async Task<IActionResult> Index(
            int? reportTypeId, int? batchId, string? importedBy,
            DateTime? from, DateTime? to,
            int page = 1, int pageSize = 25)
        {
            var query = db.ReportFiles
                .Include(f => f.ReportType)
                .Include(f => f.ReportBatch)
                .AsQueryable();

            if (reportTypeId.HasValue)
                query = query.Where(f => f.ReportTypeId == reportTypeId.Value);

            if (batchId.HasValue)
                query = query.Where(f => f.ReportBatchId == batchId.Value);

            if (!string.IsNullOrWhiteSpace(importedBy))
                query = query.Where(f => f.ImportedBy != null &&
                    f.ImportedBy.Contains(importedBy));

            if (from.HasValue)
                query = query.Where(f => f.UploadedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(f => f.UploadedAt < to.Value.AddDays(1));

            var total = await query.CountAsync();

            var files = await query
                .OrderByDescending(f => f.UploadedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Popolamento filtri
            ViewBag.ReportTypes = new SelectList(
                await db.ReportTypes.OrderBy(t => t.Key).ToListAsync(),
                "Id", "Key", reportTypeId);

            ViewBag.Batches = new SelectList(
                await db.ReportBatches.AsNoTracking().OrderByDescending(b => b.Id).ToListAsync(),
                "Id", "Name", batchId);

            ViewBag.Filters = new
            {
                reportTypeId, batchId, importedBy,
                from = from?.ToString("yyyy-MM-dd"),
                to   = to?.ToString("yyyy-MM-dd"),
                page, pageSize
            };

            ViewBag.TotalCount  = total;
            ViewBag.TotalPages  = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.CurrentPage = page;

            return View(files);
        }

        // GET /Data/Files/Import
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        public async Task<IActionResult> Import()
        {
            await PopulateImportOptionsAsync();
            return View(new SingleReportImportViewModel());
        }

        // POST /Data/Files/Import
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        [RequestSizeLimit(100 * 1024 * 1024)]
        public async Task<IActionResult> Import(
            SingleReportImportViewModel model,
            CancellationToken cancellationToken)
        {
            if (model.File is null || model.File.Length == 0)
                ModelState.AddModelError(nameof(model.File), "Selezionare un file di report.");

            if (model.BatchId.HasValue &&
                !await db.ReportBatches.AnyAsync(b => b.Id == model.BatchId.Value, cancellationToken))
            {
                ModelState.AddModelError(nameof(model.BatchId), "Il batch selezionato non esiste.");
            }

            if (!ModelState.IsValid)
                return await ImportViewAsync(model);

            var file = model.File!;
            var fileName = Path.GetFileName(file.FileName);
            var catalog = pluginCatalog.Current;
            var candidates = new List<IReportImporter>();

            foreach (var candidate in catalog.Plugins.Where(candidate =>
                         string.Equals(
                             candidate.SupportedFileExtension,
                             Path.GetExtension(fileName),
                             StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (await candidate.CanImportAsync(fileName))
                        candidates.Add(candidate);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Errore riconoscimento file {FileName} con plugin {PluginName}.",
                        fileName,
                        candidate.Name);
                }
            }

            if (candidates.Count != 1)
            {
                var description = candidates.Count == 0
                    ? "Nessun plugin disponibile riconosce il file selezionato."
                    : $"Il file e riconosciuto da piu plugin: {string.Join(", ", candidates.Select(p => p.Name))}.";

                if (candidates.Count > 1)
                {
                    logger.LogWarning(
                        "Importazione non avviata: il file {FileName} e riconosciuto da piu plugin: {PluginNames}.",
                        fileName,
                        string.Join(", ", candidates.Select(p => p.Name)));
                }

                await auditService.WriteAsync(
                    "Report.ImportRejected",
                    "Rejected",
                    entityType: "ReportFile",
                    entityId: fileName,
                    description: description,
                    details: new
                    {
                        FileName = fileName,
                        CandidatePlugins = candidates.Select(p => p.Name).ToArray()
                    });

                ModelState.AddModelError(
                    nameof(model.File),
                    description);
                return await ImportViewAsync(model);
            }

            var plugin = candidates[0];
            var validationPath = Path.Combine(catalog.Folder, $"{plugin.Name}.xml");
            if (!System.IO.File.Exists(validationPath))
            {
                logger.LogWarning(
                    "Configurazione di validazione mancante per il plugin {PluginName}: {ValidationPath}.",
                    plugin.Name,
                    validationPath);
                await auditService.WriteAsync(
                    "Report.ImportRejected",
                    "Rejected",
                    entityType: "ReportFile",
                    entityId: fileName,
                    description: $"Configurazione di validazione mancante per il plugin '{plugin.Name}'.",
                    details: new { FileName = fileName, Plugin = plugin.Name, ValidationPath = validationPath });

                ModelState.AddModelError(
                    nameof(model.File),
                    $"Configurazione di validazione mancante per il plugin '{plugin.Name}'.");
                return await ImportViewAsync(model);
            }

            try
            {
                await using var validationStream = System.IO.File.OpenRead(validationPath);
                var validationConfig = ValidationConfig.Load(validationStream);

                await using var fileStream = file.OpenReadStream();
                var importResult = await plugin.ImportAsync(
                    fileStream,
                    validationConfig,
                    ct: cancellationToken);

                if (!importResult.Success)
                {
                    await auditService.WriteAsync(
                        "Report.ImportFailed",
                        "Failed",
                        entityType: "ReportFile",
                        entityId: fileName,
                        description: importResult.Errors.FirstOrDefault() ?? "Il plugin non ha completato l'importazione.",
                        details: new { FileName = fileName, Plugin = plugin.Name, importResult.Errors });

                    foreach (var error in importResult.Errors.DefaultIfEmpty("Il plugin non ha completato l'importazione."))
                        ModelState.AddModelError(nameof(model.File), error);

                    return await ImportViewAsync(model);
                }

                if (importResult.Entities is null || !importResult.Entities.Any())
                {
                    await auditService.WriteAsync(
                        "Report.ImportFailed",
                        "Failed",
                        entityType: "ReportFile",
                        entityId: fileName,
                        description: "Il plugin non ha prodotto dati da salvare.",
                        details: new { FileName = fileName, Plugin = plugin.Name });

                    ModelState.AddModelError(nameof(model.File), "Il plugin non ha prodotto dati da salvare.");
                    return await ImportViewAsync(model);
                }

                var fileItem = new ImportFileItem(
                    fileName,
                    file.Length,
                    DateTime.UtcNow,
                    plugin.Name,
                    $"RRDA.Web upload: {fileName}");

                var saved = await importResultRepository.SaveAsync(
                    file: fileItem,
                    importResult: importResult,
                    logger: message => logger.LogInformation("{ImportMessage}", message),
                    user: User.Identity?.Name,
                    batchId: model.BatchId,
                    duplicateStrategy: model.DuplicateStrategy,
                    cancellationToken: cancellationToken);

                TempData["Success"] =
                    $"File '{fileName}' importato con plugin '{plugin.Name}': "
                    + $"{saved.EntitiesSaved} entita e {saved.PropertiesSaved} proprieta.";

                await auditService.WriteAsync(
                    "Report.ImportSucceeded",
                    "Success",
                    entityType: "ReportFile",
                    entityId: saved.ReportFileId.ToString(),
                    description: $"Importato '{fileName}' con plugin '{plugin.Name}'.",
                    details: new
                    {
                        FileName = fileName,
                        Plugin = plugin.Name,
                        PluginVersion = plugin.Version,
                        model.BatchId,
                        DuplicateStrategy = model.DuplicateStrategy.ToString(),
                        saved.EntitiesSaved,
                        saved.PropertiesSaved
                    },
                    cancellationToken: cancellationToken);

                return RedirectToAction(nameof(Details), new { id = saved.ReportFileId });
            }
            catch (DuplicateImportException ex)
            {
                await auditService.WriteAsync(
                    "Report.ImportBlocked",
                    "Blocked",
                    entityType: "ReportFile",
                    description: ex.Message,
                    details: new { FileName = fileName, Plugin = plugin.Name },
                    cancellationToken: cancellationToken);
                ModelState.AddModelError(nameof(model.File), ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Errore durante l'importazione Web del file {FileName}.", fileName);
                await auditService.WriteAsync(
                    "Report.ImportFailed",
                    "Failed",
                    entityType: "ReportFile",
                    description: ex.GetBaseException().Message,
                    details: new { FileName = fileName, Plugin = plugin.Name },
                    cancellationToken: cancellationToken);
                ModelState.AddModelError(
                    nameof(model.File),
                    $"Importazione non completata: {ex.GetBaseException().Message}");
            }

            return await ImportViewAsync(model);
        }

        // ── GET /Data/Files/Details/{id} ──────────────────────────────────
        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
        {
            var file = await db.ReportFiles
                .AsNoTracking()
                .Include(f => f.ReportType)
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

            if (file is null) return NotFound();

            var entityKinds = await db.ReportEntities
                .AsNoTracking()
                .Where(e => e.ReportFileId == id)
                .GroupBy(e => e.ReportSheet)
                .Select(g => new FileEntityKindViewModel
                {
                    Kind = g.Key,
                    Count = g.Count()
                })
                .OrderBy(item => item.Kind)
                .ToListAsync(cancellationToken);

            return View(new FileDetailsViewModel
            {
                File = file,
                EntityKinds = entityKinds,
                EntityCount = entityKinds.Sum(item => item.Count)
            });
        }

        // ── GET /Data/Files/Entities/{fileId} ─────────────────────────────
        public async Task<IActionResult> Entities(
            int fileId, string? kind,
            int page = 1, int pageSize = 50)
        {
            var file = await db.ReportFiles
                .Include(f => f.ReportType)
                .FirstOrDefaultAsync(f => f.Id == fileId);

            if (file is null) return NotFound();

            var query = db.ReportEntities
                .Where(e => e.ReportFileId == fileId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(kind))
                query = query.Where(e => e.ReportSheet == kind);

            var total = await query.CountAsync();

            var entities = await query
                .OrderBy(e => e.ReportSheet)
                .ThenBy(e => e.Key)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(e => e.Properties)
                .ToListAsync();

            ViewBag.File        = file;
            ViewBag.KindFilter  = kind;
            ViewBag.TotalCount  = total;
            ViewBag.TotalPages  = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.CurrentPage = page;
            ViewBag.Kinds       = await db.ReportEntities
                .Where(e => e.ReportFileId == fileId)
                .Select(e => e.ReportSheet)
                .Distinct()
                .OrderBy(k => k)
                .ToListAsync();

            return View(entities);
        }

        // ── POST /Data/Files/Delete/{id} ──────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        public async Task<IActionResult> Delete(int id)
        {
            var file = await db.ReportFiles.FindAsync(id);
            if (file is null) return NotFound();

            var fileName = file.FileName;
            db.ReportFiles.Remove(file);
            await db.SaveChangesAsync();

            await auditService.WriteAsync(
                "Report.Deleted",
                "Success",
                entityType: "ReportFile",
                entityId: id.ToString(),
                description: $"Eliminato il file importato '{fileName}'.",
                details: new { FileName = fileName });

            TempData["Success"] = $"File '{fileName}' eliminato.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        public async Task<IActionResult> DeleteSelected(List<int>? selectedIds)
        {
            var ids = selectedIds?.Distinct().ToList() ?? [];
            if (ids.Count == 0)
            {
                TempData["Warning"] = "Selezionare almeno un file da eliminare.";
                return RedirectToAction(nameof(Index));
            }

            var files = await db.ReportFiles
                .Where(file => ids.Contains(file.Id))
                .ToListAsync();

            if (files.Count == 0)
            {
                TempData["Warning"] = "Nessuno dei file selezionati è stato trovato.";
                return RedirectToAction(nameof(Index));
            }

            var deletedFiles = files
                .Select(file => new { file.Id, file.FileName })
                .ToArray();

            db.ReportFiles.RemoveRange(files);
            await db.SaveChangesAsync();

            await auditService.WriteAsync(
                "Report.BulkDeleted",
                "Success",
                entityType: "ReportFile",
                description: $"Eliminati {files.Count} file importati.",
                details: new
                {
                    Count = files.Count,
                    Files = deletedFiles
                });

            TempData["Success"] = $"Eliminati {files.Count} file importati.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<IActionResult> ImportViewAsync(SingleReportImportViewModel model)
        {
            await PopulateImportOptionsAsync(model.BatchId);
            return View(nameof(Import), model);
        }

        private async Task PopulateImportOptionsAsync(int? selectedBatchId = null)
        {
            ViewBag.Batches = new SelectList(
                await db.ReportBatches
                    .AsNoTracking()
                    .OrderByDescending(b => b.Id)
                    .ToListAsync(),
                "Id",
                "Name",
                selectedBatchId);
        }
    }

    public sealed class SingleReportImportViewModel
    {
        public IFormFile? File { get; set; }
        public int? BatchId { get; set; }
        public DuplicateImportStrategy DuplicateStrategy { get; set; } = DuplicateImportStrategy.Block;
    }
}
