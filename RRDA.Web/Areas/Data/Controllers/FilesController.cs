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

            var reportTypeOptions = new SelectList(
                await db.ReportTypes.OrderBy(t => t.Key).ToListAsync(),
                "Id", "Key", reportTypeId).ToList();

            var batchOptions = new SelectList(
                await db.ReportBatches.AsNoTracking().OrderByDescending(b => b.Id).ToListAsync(),
                "Id", "Name", batchId).ToList();

            return View(new FilesIndexViewModel
            {
                Files = files,
                ReportTypeOptions = reportTypeOptions,
                BatchOptions = batchOptions,
                Filters = new FilesIndexFilters
                {
                    ReportTypeId = reportTypeId,
                    BatchId = batchId,
                    ImportedBy = importedBy,
                    From = from?.ToString("yyyy-MM-dd"),
                    To = to?.ToString("yyyy-MM-dd"),
                    PageSize = pageSize
                },
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                CurrentPage = page
            });
        }

        // GET /Data/Files/Import
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        public async Task<IActionResult> Import()
        {
            return await ImportViewAsync(new SingleReportImportViewModel());
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

            var references = await BuildReferencesAsync(file, cancellationToken);
            var canManageReferences = CanManageReferences();
            var allowedTargetKinds = AllowedTargetKinds(file.ReportType.SubjectKind);
            var manualTargets = canManageReferences
                ? await db.ReportFiles
                    .AsNoTracking()
                    .Include(candidate => candidate.ReportType)
                    .Where(candidate => candidate.Id != id
                        && allowedTargetKinds.Contains(candidate.ReportType.SubjectKind))
                    .OrderByDescending(candidate => candidate.UploadedAt)
                    .ToListAsync(cancellationToken)
                : [];

            return View(new FileDetailsViewModel
            {
                File = file,
                EntityKinds = entityKinds,
                EntityCount = entityKinds.Sum(item => item.Count),
                References = references,
                CanManageReferences = canManageReferences,
                ManualReferenceTargets = manualTargets
                    .Select(candidate => new SelectListItem(
                        $"{candidate.ReportType.Key} · {candidate.FileName}",
                        candidate.Id.ToString()))
                    .ToList()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        public async Task<IActionResult> AddManualReference(
            int sourceReportFileId,
            int targetReportFileId,
            CancellationToken cancellationToken)
        {
            var source = await db.ReportFiles
                .Include(file => file.ReportType)
                .FirstOrDefaultAsync(file => file.Id == sourceReportFileId, cancellationToken);
            var target = await db.ReportFiles
                .Include(file => file.ReportType)
                .FirstOrDefaultAsync(file => file.Id == targetReportFileId, cancellationToken);

            if (source is null || target is null)
                return NotFound();

            if (!IsAllowedReference(source.ReportType.SubjectKind, target.ReportType.SubjectKind))
            {
                TempData["Warning"] = "La correlazione selezionata non è ammessa.";
                return RedirectToAction(nameof(Details), new { id = sourceReportFileId });
            }

            var exists = await db.ReportReferences.AnyAsync(
                reference => reference.SourceReportFileId == sourceReportFileId
                    && reference.TargetReportFileId == targetReportFileId,
                cancellationToken);
            if (!exists)
            {
                db.ReportReferences.Add(new ReportReference
                {
                    SourceReportFile = source,
                    TargetReportFile = target,
                    Origin = ReportReferenceOrigin.Manual,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = User.Identity?.Name
                });
                await db.SaveChangesAsync(cancellationToken);

                await auditService.WriteAsync(
                    "ReportReference.Created",
                    "Success",
                    entityType: "ReportReference",
                    description: $"Collegato manualmente '{source.FileName}' a '{target.FileName}'.",
                    details: new { SourceReportFileId = source.Id, TargetReportFileId = target.Id },
                    cancellationToken: cancellationToken);
            }

            return RedirectToAction(nameof(Details), new { id = sourceReportFileId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        public async Task<IActionResult> DeleteManualReference(int id, CancellationToken cancellationToken)
        {
            var reference = await db.ReportReferences
                .FirstOrDefaultAsync(
                    item => item.Id == id && item.Origin == ReportReferenceOrigin.Manual,
                    cancellationToken);
            if (reference is null)
                return NotFound();

            var sourceReportFileId = reference.SourceReportFileId;
            db.ReportReferences.Remove(reference);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.WriteAsync(
                "ReportReference.Deleted",
                "Success",
                entityType: "ReportReference",
                entityId: id.ToString(),
                details: new { reference.SourceReportFileId, reference.TargetReportFileId },
                cancellationToken: cancellationToken);

            return RedirectToAction(nameof(Details), new { id = sourceReportFileId });
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

            var kinds = await db.ReportEntities
                .Where(e => e.ReportFileId == fileId)
                .Select(e => e.ReportSheet)
                .Distinct()
                .OrderBy(k => k)
                .ToListAsync();

            return View(new FileEntitiesViewModel
            {
                File = file,
                Entities = entities,
                Kinds = kinds,
                KindFilter = kind,
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                CurrentPage = page
            });
        }

        // ── POST /Data/Files/Delete/{id} ──────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AtLeastSupervisor)]
        public async Task<IActionResult> Delete(int id)
        {
            var file = await db.ReportFiles.FindAsync(id);
            if (file is null) return NotFound();

            var fileName = file.FileName;
            var incomingReferences = await db.ReportReferences
                .Where(reference => reference.TargetReportFileId == id)
                .ToListAsync();
            db.ReportReferences.RemoveRange(incomingReferences);
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

            var incomingReferences = await db.ReportReferences
                .Where(reference => reference.TargetReportFileId.HasValue
                    && ids.Contains(reference.TargetReportFileId.Value))
                .ToListAsync();
            db.ReportReferences.RemoveRange(incomingReferences);
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
            model.BatchOptions = await GetImportOptionsAsync(model.BatchId);
            return View(nameof(Import), model);
        }

        private async Task<IReadOnlyList<SelectListItem>> GetImportOptionsAsync(int? selectedBatchId = null)
        {
            return new SelectList(
                await db.ReportBatches
                    .AsNoTracking()
                    .OrderByDescending(b => b.Id)
                    .ToListAsync(),
                "Id",
                "Name",
                selectedBatchId).ToList();
        }

        private async Task<IReadOnlyList<FileReferenceViewModel>> BuildReferencesAsync(
            ReportFile currentFile,
            CancellationToken cancellationToken)
        {
            var subjectKeys = await db.ReportEntities
                .AsNoTracking()
                .Where(entity => entity.ReportFileId == currentFile.Id)
                .SelectMany(entity => entity.Properties
                    .Where(property => property.Name == "value" && property.IsSubjectKey)
                    .Select(property => property.Value))
                .Where(value => value != null && value != string.Empty)
                .Distinct()
                .ToListAsync(cancellationToken);

            var references = await db.ReportReferences
                .AsNoTracking()
                .Where(reference => reference.SourceReportFileId == currentFile.Id
                    || reference.TargetReportFileId == currentFile.Id
                    || (reference.TargetReportTypeId == currentFile.ReportTypeId
                        && reference.TargetKeyValue != null
                        && subjectKeys.Contains(reference.TargetKeyValue)))
                .Include(reference => reference.SourceReportEntity)
                .Include(reference => reference.SourceReportFile)
                    .ThenInclude(file => file.ReportType)
                .Include(reference => reference.TargetReportFile!)
                    .ThenInclude(file => file.ReportType)
                .Include(reference => reference.TargetReportType)
                .OrderBy(reference => reference.Origin)
                .ThenBy(reference => reference.Id)
                .ToListAsync(cancellationToken);

            var result = new List<FileReferenceViewModel>();
            foreach (var reference in references)
            {
                var targets = new List<ReportReferenceTargetViewModel>();
                var isIncoming = reference.SourceReportFileId != currentFile.Id;
                if (isIncoming)
                {
                    targets.Add(ToReferenceTarget(reference.SourceReportFile));
                }
                else if (reference.TargetReportFile is not null)
                {
                    targets.Add(ToReferenceTarget(reference.TargetReportFile));
                }
                else if (reference.TargetReportTypeId.HasValue
                    && !string.IsNullOrWhiteSpace(reference.TargetKeyField)
                    && !string.IsNullOrWhiteSpace(reference.TargetKeyValue))
                {
                    var candidateKeys = await db.ReportEntities
                        .AsNoTracking()
                        .Where(entity => entity.ReportFile.ReportTypeId == reference.TargetReportTypeId
                            && entity.Key == reference.TargetKeyField)
                        .SelectMany(entity => entity.Properties
                            .Where(property => property.Name == "value")
                            .Select(property => new
                            {
                                entity.ReportFileId,
                                property.Value
                            }))
                        .ToListAsync(cancellationToken);
                    var matchingFileIds = candidateKeys
                        .Where(candidate => ReportReferenceKeyComparer.Equals(
                            candidate.Value,
                            reference.TargetKeyValue))
                        .Select(candidate => candidate.ReportFileId)
                        .Distinct()
                        .ToList();
                    var matchingFiles = await db.ReportFiles
                        .AsNoTracking()
                        .Include(candidate => candidate.ReportType)
                        .Where(candidate => matchingFileIds.Contains(candidate.Id))
                        .OrderByDescending(candidate => candidate.UploadedAt)
                        .ToListAsync(cancellationToken);
                    targets.AddRange(matchingFiles.Select(ToReferenceTarget));
                }

                result.Add(new FileReferenceViewModel
                {
                    Id = reference.Id,
                    Origin = reference.Origin,
                    IsIncoming = isIncoming,
                    SourceField = reference.SourceReportEntity?.Key,
                    TargetReportTypeKey = isIncoming
                        ? reference.SourceReportFile.ReportType.Key
                        : reference.TargetReportType?.Key
                            ?? reference.TargetReportFile?.ReportType.Key,
                    TargetKeyField = reference.TargetKeyField,
                    TargetKeyValue = isIncoming ? null : reference.TargetKeyValue,
                    Targets = targets
                });
            }

            return result;
        }

        private static ReportReferenceTargetViewModel ToReferenceTarget(ReportFile file) =>
            new()
            {
                FileId = file.Id,
                FileName = file.FileName,
                ReportTypeKey = file.ReportType.Key
            };

        private static bool IsAllowedReference(
            ReportSubjectKind source,
            ReportSubjectKind target) =>
            source switch
            {
                ReportSubjectKind.Radar => target is ReportSubjectKind.SubAssembly or ReportSubjectKind.Component,
                ReportSubjectKind.SubAssembly => target == ReportSubjectKind.Component,
                _ => false
            };

        private static ReportSubjectKind[] AllowedTargetKinds(ReportSubjectKind source) =>
            source switch
            {
                ReportSubjectKind.Radar => [ReportSubjectKind.SubAssembly, ReportSubjectKind.Component],
                ReportSubjectKind.SubAssembly => [ReportSubjectKind.Component],
                _ => []
            };

        private bool CanManageReferences()
        {
            var claim = User.FindFirst(AppUserClaimsTransformation.AppRoleClaimType)?.Value;
            return Enum.TryParse<AppUserRole>(claim, out var role)
                && role >= AppUserRole.Supervisor;
        }
    }

    public sealed class SingleReportImportViewModel
    {
        public IFormFile? File { get; set; }
        public int? BatchId { get; set; }
        public DuplicateImportStrategy DuplicateStrategy { get; set; } = DuplicateImportStrategy.Block;
        public IReadOnlyList<SelectListItem> BatchOptions { get; set; } = [];
    }
}
