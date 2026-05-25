using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;

namespace RRDA.Web.Areas.Data.Controllers
{
    public class TabularController(RRDADbContext db) : Controller
    {
        public async Task<IActionResult> Subject(int reportTypeId)
        {
            var reportType = await db.ReportTypes.FindAsync(reportTypeId);
            if (reportType is null) return NotFound();

            ViewBag.ReportType = reportType;

            var rows = await db.ReportEntities
                .AsNoTracking()
                .Where(e => e.ReportFile.ReportTypeId == reportTypeId)
                .Select(e => new TabularPreviewRow
                {
                    EntityId = e.Id,
                    EntityKey = e.Key,
                    ReportSheet = e.ReportSheet,
                    PropertiesCount = e.Properties.Count
                })
                .Take(200)
                .ToListAsync();

            return View(rows);
        }
    }        
    
    public class TabularPreviewRow
    {
        public int EntityId { get; set; }
        public string EntityKey { get; set; } = string.Empty;
        public string ReportSheet { get; set; } = string.Empty;
        public int PropertiesCount { get; set; }
    }
}
