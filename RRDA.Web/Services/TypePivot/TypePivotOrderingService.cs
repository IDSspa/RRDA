using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using System.Globalization;

namespace RRDA.Web.Services.TypePivot;

public sealed class TypePivotOrderingService(RRDADbContext db) : ITypePivotOrderingService
{
    public async Task<List<int>> OrderBySubjectKeyAsync(
        IReadOnlyCollection<int> fileIds,
        string sortDirection,
        CancellationToken cancellationToken = default)
    {
        if (fileIds.Count == 0)
            return [];

        var fileIdList = fileIds.ToList();
        var subjectKeys = await db.ReportEntities
            .AsNoTracking()
            .Where(entity => fileIdList.Contains(entity.ReportFileId))
            .SelectMany(entity => entity.Properties
                .Where(property => property.Name == "value" && property.IsSubjectKey)
                .Select(property => new { entity.ReportFileId, property.Value }))
            .ToListAsync(cancellationToken);

        var subjectKeysByFileId = subjectKeys
            .GroupBy(item => item.ReportFileId)
            .ToDictionary(group => group.Key, group => group.First().Value);
        var direction = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        var comparer = Comparer<int>.Create((leftId, rightId) =>
        {
            subjectKeysByFileId.TryGetValue(leftId, out var left);
            subjectKeysByFileId.TryGetValue(rightId, out var right);

            var comparison = CompareSubjectKeys(left, right, direction);
            return comparison != 0 ? comparison : leftId.CompareTo(rightId);
        });

        return [.. fileIdList.OrderBy(id => id, comparer)];
    }

    private static int CompareSubjectKeys(string? left, string? right, int direction)
    {
        var leftMissing = string.IsNullOrWhiteSpace(left);
        var rightMissing = string.IsNullOrWhiteSpace(right);

        if (leftMissing || rightMissing)
            return leftMissing == rightMissing ? 0 : leftMissing ? 1 : -1;

        var leftNumber = TryParseDouble(left);
        var rightNumber = TryParseDouble(right);
        var comparison = leftNumber.HasValue && rightNumber.HasValue
            ? leftNumber.Value.CompareTo(rightNumber.Value)
            : StringComparer.OrdinalIgnoreCase.Compare(left, right);

        return comparison * direction;
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
