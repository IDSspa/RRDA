using Microsoft.EntityFrameworkCore;
using RRDA.Data;

namespace RRDA.Web.Services;

public sealed class AdminBootstrapStartupService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    IAuditService auditService,
    ILogger<AdminBootstrapStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RRDADbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (await db.AppUsers.AnyAsync(user => user.Role == AppUserRole.Admin, cancellationToken))
            return;

        var windowsUsername = configuration["BootstrapAdmin:WindowsUsername"]?.Trim();
        if (string.IsNullOrWhiteSpace(windowsUsername))
        {
            logger.LogCritical(
                "Nessun utente Admin configurato. Impostare temporaneamente RRDA_BootstrapAdmin__WindowsUsername e riavviare RRDA.Web.");
            return;
        }

        var user = await db.AppUsers
            .FirstOrDefaultAsync(
                candidate => candidate.WindowsUsername == windowsUsername,
                cancellationToken);

        if (user is null)
        {
            user = new AppUser
            {
                WindowsUsername = windowsUsername,
                DisplayName = "Bootstrap administrator",
                Role = AppUserRole.Admin,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };
            db.AppUsers.Add(user);
        }
        else
        {
            user.Role = AppUserRole.Admin;
            user.IsEnabled = true;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Il bootstrap dell'Admin e il relativo audit sono un'unica operazione di sicurezza.
            await db.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(
                db,
                new AuditEventRequest(
                    "RRDA.Web",
                    "Security.AdminBootstrapped",
                    "Success",
                    UserName: windowsUsername,
                    EntityType: "AppUser",
                    EntityId: user.Id.ToString(),
                    Description: "Creato o promosso il primo utente Admin tramite configurazione bootstrap."),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogCritical(
                ex,
                "Bootstrap Admin annullato: impossibile salvare atomicamente l'utente e il relativo audit.");
            throw;
        }

        logger.LogWarning(
            "Creato il primo utente Admin {WindowsUsername} tramite configurazione bootstrap. Rimuovere ora RRDA_BootstrapAdmin__WindowsUsername.",
            windowsUsername);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
