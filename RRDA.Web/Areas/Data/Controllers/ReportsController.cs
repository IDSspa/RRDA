using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Areas.Data.Models;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Data.Controllers;

[Area("Data")]
[Authorize(Policy = Policies.AnyUser)]
public sealed class ReportsController(RRDADbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var reportTypes = await db.ReportTypes
            .AsNoTracking()
            .OrderByDescending(reportType => reportType.Files.Count())
            .ThenBy(reportType => reportType.Key)
            .Select(reportType => new ReportTypeCardViewModel
            {
                Id = reportType.Id,
                Key = reportType.Key,
                Name = reportType.Name,
                Description = reportType.Description,
                SubjectKind = reportType.SubjectKind,
                ReportCount = reportType.Files.Count()
            })
            .ToListAsync(cancellationToken);

        return View(new ReportsIndexViewModel { ReportTypes = reportTypes });
    }
}
