namespace RRDA.Core;

public sealed class ReportReferenceDto
{
    public required string TargetReportTypeKey { get; set; }
    public required string TargetKeyField { get; set; }
    public string? TargetKeyValue { get; set; }
}
