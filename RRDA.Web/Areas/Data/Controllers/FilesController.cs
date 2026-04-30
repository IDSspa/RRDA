using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AnyUser)]
    public class FilesController(RRDADbContext db) : Controller
    {
        // ── GET /Data/Files ───────────────────────────────────────────────
        public async Task<IActionResult> Index(
            int? reportTypeId, string? importedBy,
            DateTime? from, DateTime? to,
            int page = 1, int pageSize = 25)
        {
            var query = db.ReportFiles
                .Include(f => f.ReportType)
                .AsQueryable();

            if (reportTypeId.HasValue)
                query = query.Where(f => f.ReportTypeId == reportTypeId.Value);

            if (!string.IsNullOrWhiteSpace(importedBy))
                query = query.Where(f => f.ImportedBy != null &&
                    f.ImportedBy.ToLower().Contains(importedBy.ToLower()));

            if (from.HasValue)
                query = query.Where(f => f.UploadedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(f => f.UploadedAt <= to.Value.AddDays(1));

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

            ViewBag.Filters = new
            {
                reportTypeId, importedBy,
                from = from?.ToString("yyyy-MM-dd"),
                to   = to?.ToString("yyyy-MM-dd"),
                page, pageSize
            };

            ViewBag.TotalCount  = total;
            ViewBag.TotalPages  = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.CurrentPage = page;

            return View(files);
        }

        // ── GET /Data/Files/Details/{id} ──────────────────────────────────
        public async Task<IActionResult> Details(int id)
        {
            var file = await db.ReportFiles
                .Include(f => f.ReportType)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (file is null) return NotFound();

            ViewBag.EntityCount = await db.ReportEntities
                .CountAsync(e => e.ReportFileId == id);

            // Entità distinte per ReportSheet
            ViewBag.ReportSheets = await db.ReportEntities
                .Where(e => e.ReportFileId == id)
                .GroupBy(e => e.ReportSheet)
                .Select(g => new { Kind = g.Key, Count = g.Count() })
                .ToListAsync();

            return View(file);
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

            TempData["Success"] = $"File '{fileName}' eliminato.";
            return RedirectToAction(nameof(Index));
        }
    }
}
