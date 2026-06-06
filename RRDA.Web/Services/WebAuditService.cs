using Microsoft.EntityFrameworkCore;
using RRDA.Data;

namespace RRDA.Web.Services;

public interface IWebAuditService
{
    Task WriteAsync(
        string operation,
        string result,
        string? entityType = null,
        string? entityId = null,
        string? description = null,
        object? details = null,
        CancellationToken cancellationToken = default);
}

public sealed class WebAuditService(
    IDbContextFactory<RRDADbContext> dbFactory,
    IAuditService auditService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<WebAuditService> logger) : IWebAuditService
{
    public async Task WriteAsync(
        string operation,
        string result,
        string? entityType = null,
        string? entityId = null,
        string? description = null,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await auditService.WriteAsync(
                db,
                new AuditEventRequest(
                    "RRDA.Web",
                    operation,
                    result,
                    UserName: httpContext?.User.Identity?.Name,
                    EntityType: entityType,
                    EntityId: entityId,
                    Description: description,
                    Details: details,
                    CorrelationId: httpContext?.TraceIdentifier,
                    ClientIp: httpContext?.Connection.RemoteIpAddress?.ToString()),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossibile scrivere l'evento audit {Operation}.", operation);
        }
    }
}
