using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
            // Passiamo alla view il nome utente Windows per mostrare un messaggio utile
            ViewBag.WindowsUser = User.Identity?.Name ?? "Utente sconosciuto";
            return View();
        }
    }
}
