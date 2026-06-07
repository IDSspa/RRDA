using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRDA.Web.Models;

namespace RRDA.Web.Controllers
{
    /// <summary>
    /// Gestisce la pagina di accesso negato mostrata agli utenti Windows
    /// autenticati ma non presenti (o disabilitati) in AppUsers.
    /// </summary>
    [AllowAnonymous]
    public class AccessDeniedController : Controller
    {
        [Route("/AccessDenied")]
        public IActionResult Index()
        {
            return View(new AccessDeniedViewModel
            {
                WindowsUser = User.Identity?.Name ?? "Utente sconosciuto"
            });
        }
    }
}
