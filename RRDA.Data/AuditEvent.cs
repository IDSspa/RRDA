namespace RRDA.Data;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public required string Application { get; set; }
    public required string MachineName { get; set; }
    public string? UserName { get; set; }
    public required string Operation { get; set; }
    public required string Result { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Description { get; set; }
    public string? DetailsJson { get; set; }
    public string? CorrelationId { get; set; }
    public string? ClientIp { get; set; }
}
