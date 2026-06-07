using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RRDA.Core.Exporting;
using RRDA.Data;
using RRDA.Plugins.Common;
using RRDA.Web.Security;
using RRDA.Web.Services;
using RRDA.Web.Services.TypePivot;
using System.Net;
using System.Runtime.Versioning;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "RRDA_");

var isDevelopment = builder.Environment.IsDevelopment();
var skipWindowsAuthRequested =
    builder.Configuration.GetValue<bool>("DevSettings:SkipWindowsAuth");
if (skipWindowsAuthRequested && !isDevelopment)
{
    throw new InvalidOperationException(
        "DevSettings:SkipWindowsAuth può essere abilitato esclusivamente nell'ambiente Development.");
}

var skipWindowsAuth = isDevelopment && skipWindowsAuthRequested;
var simulatedUser = builder.Configuration.GetValue<string>("DevSettings:SimulatedUser");
if (skipWindowsAuth && string.IsNullOrWhiteSpace(simulatedUser))
{
    throw new InvalidOperationException(
        "DevSettings:SimulatedUser deve essere configurato quando SkipWindowsAuth è abilitato.");
}

if (OperatingSystem.IsWindows() && !isDevelopment)
{
    AddWindowsEventLog(builder.Logging);
}

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
            o.LoginPath = "/DevLogin";
            o.AccessDeniedPath = "/AccessDenied";
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
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
// Add services
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IDataExportService, DataExportService>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddScoped<IImportResultRepository, ImportResultRepository>();
builder.Services.AddSingleton<IPluginService, PluginService>();
builder.Services.AddSingleton<IPluginCatalog, PluginCatalog>();
builder.Services.AddScoped<IReportTypeSynchronizer, ReportTypeSynchronizer>();
builder.Services.AddScoped<IWebPluginManagementService, WebPluginManagementService>();
builder.Services.AddScoped<IWebAuditService, WebAuditService>();
builder.Services.AddScoped<ITypePivotDatasetService, TypePivotDatasetService>();
builder.Services.AddScoped<ITypePivotPlotService, TypePivotPlotService>();
builder.Services.AddScoped<ITypePivotOrderingService, TypePivotOrderingService>();
builder.Services.AddScoped<ITypePivotStatisticsService, TypePivotStatisticsService>();
builder.Services.AddHostedService<AdminBootstrapStartupService>();
builder.Services.AddHostedService<PluginCatalogStartupService>();

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
    app.MapGet("/DevLogin", async (HttpContext ctx) =>
    {
        if (!IsLocalDevelopmentRequest(ctx))
            return Results.NotFound();

        var claims = new[]
        {
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.Name, simulatedUser!),
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.AuthenticationMethod, "DevCookie")
        };

        var identity  = new System.Security.Claims.ClaimsIdentity(claims, "DevCookie");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        await ctx.SignInAsync("DevCookie", principal);
        return Results.Redirect("/");
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

static bool IsLocalDevelopmentRequest(HttpContext context)
{
    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
        return false;

    var host = context.Request.Host.Host;
    return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(host, out var hostIp) && IPAddress.IsLoopback(hostIp);
}

[SupportedOSPlatform("windows")]
static void AddWindowsEventLog(ILoggingBuilder logging)
{
#pragma warning disable CA1416 // Chiamato esclusivamente dopo OperatingSystem.IsWindows().
    logging.AddEventLog(settings =>
    {
        settings.LogName = "Application";
        settings.SourceName = "RRDA.Web";
        settings.Filter = (_, level) => level >= LogLevel.Warning;
    });
#pragma warning restore CA1416
}
