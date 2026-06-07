using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using System.Security.Claims;

namespace RRDA.Web.Security
{
    /// <summary>
    /// Aggiunge al <see cref="ClaimsPrincipal"/> dell'utente autenticato via Windows
    /// il claim di ruolo applicativo letto dalla tabella <c>AppUsers</c>.
    ///
    /// Viene invocata automaticamente da ASP.NET Core dopo ogni autenticazione
    /// (registrata come <see cref="IClaimsTransformation"/>).
    ///
    /// Comportamento:
    ///   - Utente presente e abilitato  → aggiunge claim "AppRole" con valore del ruolo
    ///   - Utente non presente          → nessun claim aggiunto (accesso negato dai policy)
    ///   - Utente disabilitato          → nessun claim aggiunto
    ///
    /// Il claim viene aggiunto una sola volta per sessione grazie alla
    /// verifica sul <see cref="ClaimsPrincipal"/> esistente.
    /// </summary>
    public sealed class AppUserClaimsTransformation(
        IDbContextFactory<RRDADbContext> dbFactory,
        ILogger<AppUserClaimsTransformation> logger) : IClaimsTransformation
    {
        /// <summary>Nome del claim che trasporta il ruolo applicativo.</summary>
        public const string AppRoleClaimType = "rrda:role";

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            // Evita di rielaborare se il claim è già presente (es. doppia chiamata nella stessa richiesta)
            if (principal.HasClaim(c => c.Type == AppRoleClaimType))
                return principal;

            var windowsUsername = principal.Identity?.Name;

            if (string.IsNullOrWhiteSpace(windowsUsername))
                return principal;

            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();

                // Il confronto case-insensitive è demandato alla collation SQL Server;
                // evita conversioni culture-sensitive e preserva l'uso dell'indice.
                var appUser = await db.AppUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u =>
                        u.WindowsUsername == windowsUsername &&
                        u.IsEnabled);

                if (appUser is null)
                {
                    logger.LogWarning(
                        "Accesso negato: utente '{Username}' non trovato in AppUsers o disabilitato.",
                        windowsUsername);
                    return principal;
                }

                // Aggiorna LastLoginAt in background (fire-and-forget, errori non critici)
                _ = UpdateLastLoginAsync(dbFactory, appUser.Id);

                // Costruiamo una nuova identità che aggiunge il claim al principal esistente
                var identity = new ClaimsIdentity();
                identity.AddClaim(new Claim(AppRoleClaimType, appUser.Role.ToString()));

                // Aggiunge anche il claim standard ASP.NET Core Role per compatibilità
                // con [Authorize(Roles = "...")] e User.IsInRole(...)
                identity.AddClaim(new Claim(ClaimTypes.Role, appUser.Role.ToString()));

                // DisplayName come claim aggiuntivo (utile nella UI)
                if (!string.IsNullOrWhiteSpace(appUser.DisplayName))
                    identity.AddClaim(new Claim("rrda:displayname", appUser.DisplayName));

                principal.AddIdentity(identity);

                logger.LogDebug(
                    "Utente '{Username}' autenticato con ruolo '{Role}'.",
                    windowsUsername, appUser.Role);
            }
            catch (Exception ex)
            {
                // Non bloccare la richiesta per un errore DB — l'utente risulterà
                // senza claim di ruolo e verrà bloccato dalle policy.
                logger.LogError(ex,
                    "Errore durante la trasformazione dei claim per '{Username}'.",
                    windowsUsername);
            }

            return principal;
        }

        private static async Task UpdateLastLoginAsync(
            IDbContextFactory<RRDADbContext> dbFactory, int userId)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.AppUsers
                    .Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(u => u.LastLoginAt, DateTime.UtcNow));
            }
            catch
            {
                // Ignorato intenzionalmente: l'aggiornamento di LastLoginAt
                // è informativo e non deve influire sull'accesso.
            }
        }
    }
}
