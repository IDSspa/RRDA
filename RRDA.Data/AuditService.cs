using System.Text.Json;

namespace RRDA.Data;

public interface IAuditService
{
    Task WriteAsync(
        RRDADbContext db,
        AuditEventRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AuditEventRequest(
    string Application,
    string Operation,
    string Result,
    string? UserName = null,
    string? EntityType = null,
    string? EntityId = null,
    string? Description = null,
    object? Details = null,
    string? CorrelationId = null,
    string? ClientIp = null,
    string? MachineName = null);

public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task WriteAsync(
        RRDADbContext db,
        AuditEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Application))
            throw new ArgumentException("Application e obbligatorio.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Operation))
            throw new ArgumentException("Operation e obbligatorio.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Result))
            throw new ArgumentException("Result e obbligatorio.", nameof(request));

        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = DateTime.UtcNow,
            Application = request.Application,
            MachineName = request.MachineName ?? Environment.MachineName,
            UserName = request.UserName,
            Operation = request.Operation,
            Result = request.Result,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Description = request.Description,
            DetailsJson = request.Details is null
                ? null
                : JsonSerializer.Serialize(request.Details, JsonOptions),
            CorrelationId = request.CorrelationId,
            ClientIp = request.ClientIp
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
