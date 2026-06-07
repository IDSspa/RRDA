using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Core;
using RRDA.Plugins.Common;
using RRDA.Web.Security;
using RRDA.Web.Areas.Plugins.Models;

namespace RRDA.Web.Areas.Plugins.Controllers
{
    [Area("Plugins")]
    [Authorize(Policy = Policies.AdminOnly)]
    public class ReportTypesController(
        RRDADbContext db,
        IPluginCatalog pluginCatalog) : Controller
    {
        // ── GET /Plugins/ReportTypes ──────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var types = await db.ReportTypes
                .OrderBy(t => t.Key)
                .Select(t => new ReportTypeSummary
                {
                    Id          = t.Id,
                    Key         = t.Key,
                    Name        = t.Name,
                    Description = t.Description,
                    SubjectKind = t.SubjectKind,
                    FileCount   = t.Files.Count()
                })
                .ToListAsync();

            return View(types);
        }

        // ── GET /Plugins/ReportTypes/Details/{id} ─────────────────────────
        public async Task<IActionResult> Details(int id)
        {
            var type = await db.ReportTypes
                .Include(t => t.Files.OrderByDescending(f => f.UploadedAt).Take(10))
                .FirstOrDefaultAsync(t => t.Id == id);

            if (type is null) return NotFound();

            return View(new ReportTypeDetailsViewModel
            {
                ReportType = type,
                FileCount = await db.ReportFiles.CountAsync(f => f.ReportTypeId == id)
            });
        }

        // ── GET /Plugins/ReportTypes/Create ───────────────────────────────
        public IActionResult Create() => View(new ReportType
        {
            Key   = string.Empty,
            Name  = string.Empty,
            Files = []
        });

        // ── POST /Plugins/ReportTypes/Create ──────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReportType model)
        {
            if (!ModelState.IsValid) return View(model);
            if (!TryApplyPluginSubjectKind(model)) return View(model);

            var exists = await db.ReportTypes
                .AnyAsync(t => t.Key == model.Key);

            if (exists)
            {
                ModelState.AddModelError(nameof(model.Key),
                    "Esiste già un tipo di report con questa chiave.");
                return View(model);
            }

            model.Files = [];
            db.ReportTypes.Add(model);
            await db.SaveChangesAsync();

            TempData["Success"] = $"Tipo di report '{model.Key}' creato.";
            return RedirectToAction(nameof(Index));
        }

        // ── GET /Plugins/ReportTypes/Edit/{id} ────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            var type = await db.ReportTypes.FindAsync(id);
            if (type is null) return NotFound();
            return View(type);
        }

        // ── POST /Plugins/ReportTypes/Edit/{id} ───────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ReportType model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);
            if (!TryApplyPluginSubjectKind(model)) return View(model);

            var exists = await db.ReportTypes
                .AnyAsync(t => t.Key == model.Key
                            && t.Id != id);

            if (exists)
            {
                ModelState.AddModelError(nameof(model.Key),
                    "Esiste già un tipo di report con questa chiave.");
                return View(model);
            }

            var type = await db.ReportTypes.FindAsync(id);
            if (type is null) return NotFound();

            type.Key         = model.Key;
            type.Name        = model.Name;
            type.Description = model.Description;
            type.SubjectKind = model.SubjectKind;

            await db.SaveChangesAsync();

            TempData["Success"] = $"Tipo di report '{type.Key}' aggiornato.";
            return RedirectToAction(nameof(Index));
        }

        // ── POST /Plugins/ReportTypes/Delete/{id} ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await db.ReportTypes
                .Include(t => t.Files)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (type is null) return NotFound();

            if (type.Files.Count != 0)
            {
                TempData["Warning"] =
                    $"Impossibile eliminare '{type.Key}': esistono {type.Files.Count} file associati.";
                return RedirectToAction(nameof(Index));
            }

            db.ReportTypes.Remove(type);
            await db.SaveChangesAsync();

            TempData["Success"] = $"Tipo di report '{type.Key}' eliminato.";
            return RedirectToAction(nameof(Index));
        }

        private bool TryApplyPluginSubjectKind(ReportType reportType)
        {
            var plugin = pluginCatalog.Current.Plugins.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, reportType.Key, StringComparison.OrdinalIgnoreCase));
            if (plugin is null)
            {
                ModelState.AddModelError(
                    nameof(reportType.Key),
                    $"Nessun plugin caricato espone la chiave '{reportType.Key}'.");
                return false;
            }

            reportType.SubjectKind = plugin.SubjectKind;
            return true;
        }
    }

    // ── ViewModel ─────────────────────────────────────────────────────────
    public class ReportTypeSummary
    {
        public int    Id          { get; set; }
        public string Key         { get; set; } = string.Empty;
        public string Name        { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ReportSubjectKind SubjectKind { get; set; }
        public int    FileCount   { get; set; }
    }
}
