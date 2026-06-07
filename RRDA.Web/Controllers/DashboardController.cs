using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;
using RRDA.Web.Models;

namespace RRDA.Web.Controllers
{
    [Authorize(Policy = Policies.AnyUser)]
    public class DashboardController(RRDADbContext db) : Controller
    {
        public async Task<IActionResult> Index()
        {
            var recentFiles = await db.ReportFiles
                .Include(f => f.ReportType)
                .OrderByDescending(f => f.UploadedAt)
                .Take(5)
                .ToListAsync();

            return View(new DashboardViewModel
            {
                TotalFiles = await db.ReportFiles.CountAsync(),
                TotalBatches = await db.ReportBatches.CountAsync(),
                TotalEntities = await db.ReportEntities.CountAsync(),
                TotalReportTypes = await db.ReportTypes.CountAsync(),
                TotalUsers = await db.AppUsers.CountAsync(u => u.IsEnabled),
                RecentFiles = recentFiles
            });
        }
    }
}
