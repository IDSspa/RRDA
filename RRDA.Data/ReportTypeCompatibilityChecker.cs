using Microsoft.EntityFrameworkCore;
using RRDA.Core;

namespace RRDA.Data;

public interface IReportTypeCompatibilityChecker
{
    Task<ReportTypeCompatibilityResult> CheckAsync(
        RRDADbContext db,
        IEnumerable<IReportImporter> plugins,
        CancellationToken cancellationToken = default);
}

public sealed class ReportTypeCompatibilityChecker : IReportTypeCompatibilityChecker
{
    public async Task<ReportTypeCompatibilityResult> CheckAsync(
        RRDADbContext db,
        IEnumerable<IReportImporter> plugins,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(plugins);

        var pluginsByName = new Dictionary<string, IReportImporter>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.Name))
            {
                throw new InvalidOperationException("Impossibile verificare un plugin senza nome.");
            }

            if (!pluginsByName.TryAdd(plugin.Name, plugin))
            {
                throw new InvalidOperationException(
                    $"Sono stati caricati piu plugin con il nome '{plugin.Name}'.");
            }
        }

        if (pluginsByName.Count == 0)
        {
            return ReportTypeCompatibilityResult.Empty;
        }

        var reportTypesByKey = (await db.ReportTypes
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        var subjectKindMismatches = new List<ReportTypeSubjectKindMismatch>();

        foreach (var plugin in pluginsByName.Values)
        {
            if (!reportTypesByKey.TryGetValue(plugin.Name, out var reportType))
            {
                missing.Add(plugin.Name);
                continue;
            }

            if (reportType.SubjectKind != plugin.SubjectKind)
            {
                subjectKindMismatches.Add(new ReportTypeSubjectKindMismatch(
                    plugin.Name,
                    reportType.SubjectKind,
                    plugin.SubjectKind));
            }
        }

        return new ReportTypeCompatibilityResult(missing, subjectKindMismatches);
    }
}

public sealed record ReportTypeSubjectKindMismatch(
    string ReportTypeKey,
    ReportSubjectKind DatabaseSubjectKind,
    ReportSubjectKind PluginSubjectKind);

public sealed record ReportTypeCompatibilityResult(
    IReadOnlyList<string> MissingReportTypes,
    IReadOnlyList<ReportTypeSubjectKindMismatch> SubjectKindMismatches)
{
    public static ReportTypeCompatibilityResult Empty { get; } = new([], []);

    public bool IsCompatible => MissingReportTypes.Count == 0 && SubjectKindMismatches.Count == 0;
}
