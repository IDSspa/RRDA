using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Areas.Data.Models;
using RRDA.Web.Security;
using RRDA.Web.Services;

namespace RRDA.Web.Areas.Data.Controllers;

[Area("Data")]
[Authorize(Policy = Policies.AnyUser)]
public sealed class BatchesController(RRDADbContext db, IWebAuditService auditService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var batches = await db.ReportBatches
            .AsNoTracking()
            .OrderByDescending(batch => batch.Id)
            .Select(batch => new BatchListItemViewModel
            {
                Id = batch.Id,
                Name = batch.Name,
                Description = batch.Description,
                IsMaintenance = batch.IsMaintenance,
                ReportCount = batch.ReportFiles.Count
            })
            .ToListAsync(cancellationToken);

        return View(new BatchIndexViewModel
        {
            Batches = batches,
            CanManage = UserHasSupervisorRole()
        });
    }

    [HttpGet]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public IActionResult Create() => View(new BatchCreateViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public async Task<IActionResult> Create(BatchCreateViewModel model, CancellationToken cancellationToken)
    {
        var name = model.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            ModelState.AddModelError(nameof(model.Name), "Il nome del batch è obbligatorio.");

        if (!ModelState.IsValid)
            return View(model);

        var batch = new ReportBatch
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            IsMaintenance = model.IsMaintenance
        };

        db.ReportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            "Batch.Created",
            "Success",
            entityType: nameof(ReportBatch),
            entityId: batch.Id.ToString(),
            description: $"Creato il batch '{batch.Name}'.",
            details: new { batch.Name, batch.Description, batch.IsMaintenance },
            cancellationToken: cancellationToken);

        TempData["Success"] = $"Batch '{batch.Name}' creato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var batch = await db.ReportBatches
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new BatchDeleteViewModel
            {
                Id = item.Id,
                Name = item.Name,
                ReportCount = item.ReportFiles.Count
            })
            .SingleOrDefaultAsync(cancellationToken);

        return batch is null ? NotFound() : View(batch);
    }

    [HttpPost, ActionName(nameof(Delete)), ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public async Task<IActionResult> DeleteConfirmed(
        BatchDeleteViewModel model,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(model.Strategy))
            ModelState.AddModelError(nameof(model.Strategy), "Selezionare una modalità di cancellazione valida.");

        var batch = await db.ReportBatches
            .Include(item => item.ReportFiles)
            .SingleOrDefaultAsync(item => item.Id == model.Id, cancellationToken);

        if (batch is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            model.Name = batch.Name;
            model.ReportCount = batch.ReportFiles.Count;
            return View(nameof(Delete), model);
        }

        var reportCount = batch.ReportFiles.Count;
        if (model.Strategy == BatchDeleteStrategy.DeleteReports)
        {
            db.ReportFiles.RemoveRange(batch.ReportFiles);
        }
        else
        {
            foreach (var reportFile in batch.ReportFiles.ToList())
            {
                reportFile.ReportBatchId = null;
                reportFile.ReportBatch = null;
            }
        }

        db.ReportBatches.Remove(batch);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            "Batch.Deleted",
            "Success",
            entityType: nameof(ReportBatch),
            entityId: batch.Id.ToString(),
            description: $"Eliminato il batch '{batch.Name}'.",
            details: new { batch.Name, ReportCount = reportCount, Strategy = model.Strategy.ToString() },
            cancellationToken: cancellationToken);

        TempData["Success"] = model.Strategy == BatchDeleteStrategy.DeleteReports
            ? $"Batch '{batch.Name}' e {reportCount} report collegati eliminati."
            : $"Batch '{batch.Name}' eliminato; {reportCount} report mantenuti senza batch.";
        return RedirectToAction(nameof(Index));
    }

    private bool UserHasSupervisorRole()
    {
        var roleClaim = User.FindFirst(AppUserClaimsTransformation.AppRoleClaimType)?.Value;
        return Enum.TryParse<AppUserRole>(roleClaim, out var role) && role >= AppUserRole.Supervisor;
    }
}
