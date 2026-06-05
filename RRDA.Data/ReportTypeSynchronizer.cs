using Microsoft.EntityFrameworkCore;
using RRDA.Core;

namespace RRDA.Data;

public interface IReportTypeSynchronizer
{
    Task<ReportTypeSyncResult> SyncAsync(
        RRDADbContext db,
        IEnumerable<IReportImporter> plugins,
        CancellationToken cancellationToken = default);
}

public sealed class ReportTypeSynchronizer : IReportTypeSynchronizer
{
    public async Task<ReportTypeSyncResult> SyncAsync(
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
                throw new InvalidOperationException("Impossibile sincronizzare un plugin senza nome.");
            }

            if (!pluginsByName.TryAdd(plugin.Name, plugin))
            {
                throw new InvalidOperationException(
                    $"Sono stati caricati piu plugin con il nome '{plugin.Name}'.");
            }
        }

        if (pluginsByName.Count == 0)
        {
            return ReportTypeSyncResult.Empty;
        }

        var existingTypes = (await db.ReportTypes
                .ToListAsync(cancellationToken))
            .ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

        var inserted = new List<string>();
        var updated = new List<string>();

        foreach (var plugin in pluginsByName.Values)
        {
            if (existingTypes.TryGetValue(plugin.Name, out var existing))
            {
                if (existing.SubjectKind != plugin.SubjectKind)
                {
                    existing.SubjectKind = plugin.SubjectKind;
                    updated.Add(plugin.Name);
                }

                continue;
            }

            db.ReportTypes.Add(new ReportType
            {
                Key = plugin.Name,
                Name = plugin.Name,
                Description = string.Empty,
                SubjectKind = plugin.SubjectKind,
                Files = [],
                TabularSessions = []
            });
            inserted.Add(plugin.Name);
        }

        if (inserted.Count > 0 || updated.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ReportTypeSyncResult(inserted, updated);
    }
}

public sealed record ReportTypeSyncResult(
    IReadOnlyList<string> Inserted,
    IReadOnlyList<string> Updated)
{
    public static ReportTypeSyncResult Empty { get; } = new([], []);

    public bool HasChanges => Inserted.Count > 0 || Updated.Count > 0;
}
