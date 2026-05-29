using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;

var builder = WebApplication.CreateBuilder(args);

var isDevelopment = builder.Environment.IsDevelopment();
var skipWindowsAuth = isDevelopment &&
    builder.Configuration.GetValue<bool>("DevSettings:SkipWindowsAuth");


// ─────────────────────────────────────────────────────────────────────────────
// Database
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<RRDADbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Scoped);

// ─────────────────────────────────────────────────────────────────────────────
// Autenticazione
// ─────────────────────────────────────────────────────────────────────────────
if (skipWindowsAuth)
{
    // Modalità sviluppo: cookie auth con utente simulato
    // L'utente simulato deve esistere in AppUsers con ruolo Admin.
    builder.Services.AddAuthentication("DevCookie")
        .AddCookie("DevCookie", o =>
        {
            o.LoginPath              = "/DevLogin";
            o.AccessDeniedPath  = "/AccessDenied";
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
        });
}
else
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}

builder.Services.AddScoped<IClaimsTransformation, AppUserClaimsTransformation>();

// ─────────────────────────────────────────────────────────────────────────────
// Autorizzazione
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAuthorizationHandler, MinimumRoleHandler>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.AnyUser, policy =>
        policy.AddRequirements(new MinimumRoleRequirement(AppUserRole.Operator)))
    .AddPolicy(Policies.AtLeastSupervisor, policy =>
        policy.AddRequirements(new MinimumRoleRequirement(AppUserRole.Supervisor)))
    .AddPolicy(Policies.AdminOnly, policy =>
        policy.AddRequirements(new MinimumRoleRequirement(AppUserRole.Admin)));

// ─────────────────────────────────────────────────────────────────────────────
// MVC
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
// Add services
builder.Services.AddMemoryCache();

var app = builder.Build();

// ─────────────────────────────────────────────────────────────────────────────
// Pipeline HTTP
// ─────────────────────────────────────────────────────────────────────────────
if (!isDevelopment)
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection(); // redirect HTTPS solo in produzione
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ─────────────────────────────────────────────────────────────────────────────
// Endpoint login sviluppo (solo se SkipWindowsAuth = true)
// Navigare su /DevLogin per ottenere il cookie di sessione.
// RIMUOVERE prima del deploy in produzione.
// ─────────────────────────────────────────────────────────────────────────────
if (skipWindowsAuth)
{
    app.MapGet("/DevLogin", async (HttpContext ctx, IConfiguration cfg) =>
    {
        var devUser = cfg.GetValue<string>("DevSettings:SimulatedUser")
                      ?? "DEV\\developer";

        var claims = new[]
        {
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.Name, devUser),
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.AuthenticationMethod, "DevCookie")
        };

        var identity  = new System.Security.Claims.ClaimsIdentity(claims, "DevCookie");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        await ctx.SignInAsync("DevCookie", principal);
        ctx.Response.Redirect("/");
    }).AllowAnonymous();
}

// ─────────────────────────────────────────────────────────────────────────────
// Routing
// ─────────────────────────────────────────────────────────────────────────────
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
