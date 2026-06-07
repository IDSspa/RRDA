using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;
using RRDA.Web.Models;

namespace RRDA.Web.Controllers
{
    [Authorize(Policy = Policies.AnyUser)]
    public class DashboardController(IDbContextFactory<RRDADbContext> dbFactory) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var recentFilesTask = LoadRecentFilesAsync(cancellationToken);
            var totalFilesTask = CountAsync(db => db.ReportFiles.CountAsync(cancellationToken), cancellationToken);
            var totalBatchesTask = CountAsync(db => db.ReportBatches.CountAsync(cancellationToken), cancellationToken);
            var totalEntitiesTask = CountAsync(db => db.ReportEntities.CountAsync(cancellationToken), cancellationToken);
            var totalReportTypesTask = CountAsync(db => db.ReportTypes.CountAsync(cancellationToken), cancellationToken);
            var totalUsersTask = CountAsync(db => db.AppUsers.CountAsync(
                user => user.IsEnabled,
                cancellationToken), cancellationToken);

            await Task.WhenAll(
                recentFilesTask,
                totalFilesTask,
                totalBatchesTask,
                totalEntitiesTask,
                totalReportTypesTask,
                totalUsersTask);

            return View(new DashboardViewModel
            {
                TotalFiles = await totalFilesTask,
                TotalBatches = await totalBatchesTask,
                TotalEntities = await totalEntitiesTask,
                TotalReportTypes = await totalReportTypesTask,
                TotalUsers = await totalUsersTask,
                RecentFiles = await recentFilesTask
            });
        }

        private async Task<int> CountAsync(
            Func<RRDADbContext, Task<int>> count,
            CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await count(db);
        }

        private async Task<List<ReportFile>> LoadRecentFilesAsync(CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.ReportFiles
                .AsNoTracking()
                .Include(file => file.ReportType)
                .OrderByDescending(file => file.UploadedAt)
                .Take(5)
                .ToListAsync(cancellationToken);
        }
    }
}
