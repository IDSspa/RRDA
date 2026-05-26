using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = Policies.AdminOnly)]
    public class SettingsController(IConfiguration configuration) : Controller
    {
        private const string DecimalPlacesCookieName = "RRDA_TypePivot_DecimalPlaces";
        private const int DefaultDecimalPlaces = 4;
        private const int MaxDecimalPlaces = 15;
        
        [HttpGet]
        public IActionResult Index()
        {
            var configured = configuration.GetValue<int?>("TypePivot:DecimalPlaces") ?? DefaultDecimalPlaces;
            var effective = configured;

            if (Request.Cookies.TryGetValue(DecimalPlacesCookieName, out var cookieValue)
                && int.TryParse(cookieValue, out var parsed))
            {
                effective = parsed;
            }

            var model = new SettingsViewModel
            {
                TypePivotDecimalPlaces = Math.Clamp(effective, 0, MaxDecimalPlaces)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(SettingsViewModel model)
        {
            model.TypePivotDecimalPlaces = Math.Clamp(model.TypePivotDecimalPlaces, 0, MaxDecimalPlaces);

            Response.Cookies.Append(DecimalPlacesCookieName,
                model.TypePivotDecimalPlaces.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });

            TempData["Success"] = "Impostazione salvata correttamente.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class SettingsViewModel
    {
        public int TypePivotDecimalPlaces { get; set; }
    }
}
