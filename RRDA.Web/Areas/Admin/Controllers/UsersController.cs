using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = Policies.AdminOnly)]
    public class UsersController(RRDADbContext db) : Controller
    {
        // ── GET /Admin/Users ──────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var users = await db.AppUsers
                .OrderBy(u => u.WindowsUsername)
                .ToListAsync();
            return View(users);
        }

        // ── GET /Admin/Users/Create ───────────────────────────────────────
        public IActionResult Create() => View(new AppUser { WindowsUsername = string.Empty });

        // ── POST /Admin/Users/Create ──────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AppUser model)
        {
            if (!ModelState.IsValid) return View(model);

            // Verifica duplicato
            var exists = await db.AppUsers
                .AnyAsync(u => u.WindowsUsername == model.WindowsUsername);

            if (exists)
            {
                ModelState.AddModelError(nameof(model.WindowsUsername),
                    "Esiste già un utente con questo username.");
                return View(model);
            }

            model.CreatedAt = DateTime.UtcNow;
            db.AppUsers.Add(model);
            await db.SaveChangesAsync();

            TempData["Success"] = $"Utente '{model.WindowsUsername}' creato.";
            return RedirectToAction(nameof(Index));
        }

        // ── GET /Admin/Users/Edit/{id} ────────────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            var user = await db.AppUsers.FindAsync(id);
            if (user is null) return NotFound();
            return View(user);
        }

        // ── POST /Admin/Users/Edit/{id} ───────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AppUser model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);

            // Verifica duplicato su altri utenti
            var exists = await db.AppUsers
                .AnyAsync(u => u.WindowsUsername == model.WindowsUsername
                            && u.Id != id);

            if (exists)
            {
                ModelState.AddModelError(nameof(model.WindowsUsername),
                    "Esiste già un utente con questo username.");
                return View(model);
            }

            var user = await db.AppUsers.FindAsync(id);
            if (user is null) return NotFound();

            // Impedisce all'Admin corrente di rimuoversi i privilegi
            var currentUsername = User.Identity?.Name ?? string.Empty;
            if (string.Equals(user.WindowsUsername, currentUsername,
                    StringComparison.OrdinalIgnoreCase)
                && model.Role != AppUserRole.Admin)
            {
                TempData["Warning"] = "Non puoi rimuovere il ruolo Admin al tuo account.";
                return RedirectToAction(nameof(Index));
            }

            user.WindowsUsername = model.WindowsUsername;
            user.DisplayName     = model.DisplayName;
            user.Role            = model.Role;
            user.IsEnabled       = model.IsEnabled;

            await db.SaveChangesAsync();

            TempData["Success"] = $"Utente '{user.WindowsUsername}' aggiornato.";
            return RedirectToAction(nameof(Index));
        }

        // ── POST /Admin/Users/Delete/{id} ─────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await db.AppUsers.FindAsync(id);
            if (user is null) return NotFound();

            // Impedisce cancellazione del proprio account
            var currentUsername = User.Identity?.Name ?? string.Empty;
            if (string.Equals(user.WindowsUsername, currentUsername,
                    StringComparison.OrdinalIgnoreCase))
            {
                TempData["Warning"] = "Non puoi eliminare il tuo account.";
                return RedirectToAction(nameof(Index));
            }

            db.AppUsers.Remove(user);
            await db.SaveChangesAsync();

            TempData["Success"] = $"Utente '{user.WindowsUsername}' eliminato.";
            return RedirectToAction(nameof(Index));
        }
    }
}
