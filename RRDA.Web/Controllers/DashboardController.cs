using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;

namespace RRDA.Web.Controllers
{
    [Authorize(Policy = Policies.AnyUser)]
    public class DashboardController(RRDADbContext db) : Controller
    {
        public async Task<IActionResult> Index()
        {
            ViewBag.TotalFiles      = await db.ReportFiles.CountAsync();
            ViewBag.TotalBatches    = await db.ReportBatches.CountAsync();
            ViewBag.TotalEntities   = await db.ReportEntities.CountAsync();
            ViewBag.TotalReportTypes= await db.ReportTypes.CountAsync();
            ViewBag.TotalUsers      = await db.AppUsers.CountAsync(u => u.IsEnabled);

            // Ultimi 5 file importati
            ViewBag.RecentFiles = await db.ReportFiles
                .Include(f => f.ReportType)
                .OrderByDescending(f => f.UploadedAt)
                .Take(5)
                .ToListAsync();

            return View();
        }
    }
}
