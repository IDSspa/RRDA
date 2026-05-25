using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Data.Controllers
{
    [Area("Data")]
    [Authorize(Policy = Policies.AtLeastSupervisor)]
    public class StatisticsController : Controller
    {
        public IActionResult Subject(int reportTypeId)
        {
            ViewBag.ReportTypeId = reportTypeId;
            return View();
        }
    }
}
