using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RRDA.Data;
using RRDA.Web.Areas.Data.Controllers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RRDA.Web.Services.TypePivot;

public sealed class TypePivotStatisticsService(
    RRDADbContext db,
    IMemoryCache cache) : ITypePivotStatisticsService
{
    private const int CacheDurationMinutes = 10;
    private const string CacheKeyPrefix = "TypePivot_Stats_";

    public async Task<Dictionary<string, TypePivotColumnStatistics>> GetAsync(
        IReadOnlyCollection<int> fileIds,
        IReadOnlyCollection<string> visibleHeaders,
        CancellationToken cancellationToken = default)
    {
        var fileIdList = fileIds.ToList();
        var visibleHeaderSet = visibleHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cacheKey = GenerateCacheKey(fileIdList, visibleHeaderSet);
        if (cache.TryGetValue(cacheKey, out Dictionary<string, TypePivotColumnStatistics>? cached))
            return cached ?? [];

        var pairs = fileIdList.Count == 0 ? [] : await db.ReportEntities
            .AsNoTracking()
            .Where(e => fileIdList.Contains(e.ReportFileId))
            .SelectMany(e => e.Properties
                .Where(p => p.Name == "value" && !p.IsSubjectKey)
                .Select(p => new { e.Key, p.Value }))
            .ToListAsync(cancellationToken);

        var statistics = pairs
            .Where(p => visibleHeaderSet.Contains(p.Key))
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Calculate(group.Select(p => p.Value)),
                StringComparer.OrdinalIgnoreCase);

        cache.Set(cacheKey, statistics, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheDurationMinutes)
        });
        return statistics;
    }

    private static TypePivotColumnStatistics Calculate(IEnumerable<string?> rawValues)
    {
        var values = rawValues
            .Select(TryParseDouble)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Where(double.IsFinite)
            .OrderBy(value => value)
            .ToList();

        if (values.Count == 0)
            return new TypePivotColumnStatistics();

        var mean = values.Average();
        var median = values.Count % 2 == 1
            ? values[values.Count / 2]
            : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
        var variance = values.Count > 1
            ? values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1)
            : 0;

        return new TypePivotColumnStatistics
        {
            Count = values.Count,
            Mean = mean,
            Median = median,
            StandardDeviation = Math.Sqrt(variance),
            Min = values.First(),
            Max = values.Last()
        };
    }

    private static string GenerateCacheKey(
        IReadOnlyCollection<int> fileIds,
        IReadOnlyCollection<string> visibleHeaders)
    {
        var input = string.Join(",", fileIds.OrderBy(id => id))
            + "|"
            + string.Join(",", visibleHeaders.OrderBy(header => header, StringComparer.OrdinalIgnoreCase));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return CacheKeyPrefix + Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-");
    }

    private static double? TryParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue))
            return invariantValue;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentValue))
            return currentValue;
        return null;
    }
}
