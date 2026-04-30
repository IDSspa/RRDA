using Microsoft.AspNetCore.Authorization;
using RRDA.Data;

namespace RRDA.Web.Security
{
    /// <summary>
    /// Nomi delle policy di autorizzazione usate nei controller con [Authorize(Policy = ...)].
    /// </summary>
    public static class Policies
    {
        /// <summary>Richiede almeno ruolo Operator (tutti gli utenti abilitati).</summary>
        public const string AnyUser = "AnyUser";

        /// <summary>Richiede almeno ruolo Supervisor.</summary>
        public const string AtLeastSupervisor = "AtLeastSupervisor";

        /// <summary>Richiede ruolo Admin.</summary>
        public const string AdminOnly = "AdminOnly";
    }

    /// <summary>
    /// Requirement personalizzato: verifica che il claim <c>rrda:role</c>
    /// sia maggiore o uguale al ruolo minimo richiesto.
    /// </summary>
    public sealed class MinimumRoleRequirement(AppUserRole minimumRole) : IAuthorizationRequirement
    {
        public AppUserRole MinimumRole { get; } = minimumRole;
    }

    /// <summary>
    /// Handler per <see cref="MinimumRoleRequirement"/>.
    /// Legge il claim <c>rrda:role</c> e confronta il valore numerico con il minimo richiesto.
    /// </summary>
    public sealed class MinimumRoleHandler : AuthorizationHandler<MinimumRoleRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            MinimumRoleRequirement requirement)
        {
            var roleClaim = context.User.FindFirst(AppUserClaimsTransformation.AppRoleClaimType);

            if (roleClaim is null)
            {
                // Nessun claim di ruolo → utente non in AppUsers o disabilitato
                context.Fail();
                return Task.CompletedTask;
            }

            if (Enum.TryParse<AppUserRole>(roleClaim.Value, out var userRole) &&
                userRole >= requirement.MinimumRole)
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }
    }
}
