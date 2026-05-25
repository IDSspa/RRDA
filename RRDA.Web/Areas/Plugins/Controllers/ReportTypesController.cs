using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Plugins.Controllers
{
    [Area("Plugins")]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public class ReportTypesController(RRDADbContext db) : Controller
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

            ViewBag.FileCount = await db.ReportFiles
                .CountAsync(f => f.ReportTypeId == id);

            return View(type);
        }

        // ── GET /Plugins/ReportTypes/Create ───────────────────────────────
        [Authorize(Policy = Policies.AdminOnly)]
        public IActionResult Create() => View(new ReportType
        {
            Key   = string.Empty,
            Name  = string.Empty,
            SubjectKind = ResolveSubjectKindFromPluginKey(string.Empty),
            Files = [],
            TabularSessions = []
        });

        // ── POST /Plugins/ReportTypes/Create ──────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> Create(ReportType model)
        {
            if (!ModelState.IsValid) return View(model);

            var exists = await db.ReportTypes
                .AnyAsync(t => t.Key.ToLower() == model.Key.ToLower());

            if (exists)
            {
                ModelState.AddModelError(nameof(model.Key),
                    "Esiste già un tipo di report con questa chiave.");
                return View(model);
            }

            model.SubjectKind = ResolveSubjectKindFromPluginKey(model.Key);
            model.Files = [];
            model.TabularSessions = [];
            db.ReportTypes.Add(model);
            await db.SaveChangesAsync();

            TempData["Success"] = $"Tipo di report '{model.Key}' creato.";
            return RedirectToAction(nameof(Index));
        }

        // ── GET /Plugins/ReportTypes/Edit/{id} ────────────────────────────
        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> Edit(int id)
        {
            var type = await db.ReportTypes.FindAsync(id);
            if (type is null) return NotFound();
            return View(type);
        }

        // ── POST /Plugins/ReportTypes/Edit/{id} ───────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> Edit(int id, ReportType model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);

            var exists = await db.ReportTypes
                .AnyAsync(t => t.Key.ToLower() == model.Key.ToLower()
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
            type.SubjectKind = ResolveSubjectKindFromPluginKey(model.Key);

            await db.SaveChangesAsync();

            TempData["Success"] = $"Tipo di report '{type.Key}' aggiornato.";
            return RedirectToAction(nameof(Index));
        }



        private static ReportSubjectKind ResolveSubjectKindFromPluginKey(string reportTypeKey)
        {
            var key = (reportTypeKey ?? string.Empty).ToUpperInvariant();

            if (key.Contains("3LIV") || key.Contains("RADAR"))
                return ReportSubjectKind.Radar;

            if (key.Contains("2LIV") || key.Contains("SUB"))
                return ReportSubjectKind.SubAssembly;

            return ReportSubjectKind.Component;
        }

        // ── POST /Plugins/ReportTypes/Delete/{id} ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await db.ReportTypes
                .Include(t => t.Files)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (type is null) return NotFound();

            if (type.Files.Any())
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
