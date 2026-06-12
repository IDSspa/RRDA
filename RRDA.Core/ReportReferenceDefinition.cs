namespace RRDA.Core;

/// <summary>
/// Declares that a workbook DefinedName contains the key of another report subject.
/// </summary>
public sealed record ReportReferenceDefinition(
    string DefinedName,
    string TargetReportTypeKey,
    string TargetKeyField);

/// <summary>
/// Optional plugin capability used when generating validation files.
/// </summary>
public interface IReportReferenceProvider
{
    IReadOnlyList<ReportReferenceDefinition> ReferenceDefinitions { get; }
}
