using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRDA.Web.Security;

namespace RRDA.Web.Controllers;

/// <summary>
/// Contenuti di supporto disponibili a tutti gli utenti RRDA autorizzati.
/// </summary>
[Authorize(Policy = Policies.AnyUser)]
public class HelpController : Controller
{
    public IActionResult Index() => View();

    public IActionResult RepImpValidators() => View();
}
