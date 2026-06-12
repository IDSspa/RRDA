namespace RRDA.Data;

public enum ReportReferenceOrigin
{
    Imported = 0,
    Manual = 1
}

/// <summary>
/// Correlation from a Radar/SubAssembly report to another report.
/// Imported references retain their logical target even when no target file exists.
/// Manual references point to a specific imported file.
/// </summary>
public sealed class ReportReference
{
    public int Id { get; set; }

    public int SourceReportFileId { get; set; }
    public required ReportFile SourceReportFile { get; set; }

    public int? SourceReportEntityId { get; set; }
    public ReportEntity? SourceReportEntity { get; set; }

    public int? TargetReportFileId { get; set; }
    public ReportFile? TargetReportFile { get; set; }

    public int? TargetReportTypeId { get; set; }
    public ReportType? TargetReportType { get; set; }

    public string? TargetKeyField { get; set; }
    public string? TargetKeyValue { get; set; }

    public ReportReferenceOrigin Origin { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}
