using RRDA.Web.Areas.Data.Controllers;
using System.Globalization;

namespace RRDA.Web.Services.TypePivot;

public static class TypePivotRangeAggregator
{
    public static List<TypePivotRangeDescriptor> BuildDescriptors(IEnumerable<PivotPair> pairs)
    {
        return pairs
            .Where(pair => pair.IsRange && !string.IsNullOrWhiteSpace(pair.RangeName))
            .GroupBy(pair => pair.RangeName!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TypePivotRangeDescriptor
            {
                Name = group.Key,
                Unit = group.Select(pair => pair.Unit).FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit)),
                ExpandedHeaders = group
                    .OrderBy(GetIndex)
                    .Select(pair => pair.Key)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(range => range.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static TypePivotRangeCell? Build(string name, string? unit, IEnumerable<PivotPair> pairs)
    {
        var points = pairs
            .Select(pair => new { Index = GetIndex(pair), Value = TryParseDouble(pair.Value) })
            .Where(point => point.Index.HasValue && point.Value.HasValue)
            .Select(point => new TypePivotRangePoint { Index = point.Index!.Value, Value = point.Value!.Value })
            .OrderBy(point => point.Index)
            .ToList();

        if (points.Count == 0)
            return null;

        var orderedValues = points.Select(point => point.Value).OrderBy(value => value).ToList();
        var mean = orderedValues.Average();
        var variance = orderedValues.Count > 1
            ? orderedValues.Sum(value => Math.Pow(value - mean, 2)) / (orderedValues.Count - 1)
            : 0;
        var median = orderedValues.Count % 2 == 1
            ? orderedValues[orderedValues.Count / 2]
            : (orderedValues[(orderedValues.Count / 2) - 1] + orderedValues[orderedValues.Count / 2]) / 2;
        var min = points.MinBy(point => point.Value)!;
        var max = points.MaxBy(point => point.Value)!;

        return new TypePivotRangeCell
        {
            Name = name,
            Unit = unit,
            Points = points,
            Min = min.Value,
            MinIndex = min.Index,
            Max = max.Value,
            MaxIndex = max.Index,
            Mean = mean,
            Median = median,
            StandardDeviation = Math.Sqrt(variance)
        };
    }

    public static int? GetIndex(PivotPair pair)
    {
        if (int.TryParse(pair.RowIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) && row > 0)
            return row;
        if (int.TryParse(pair.ColIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out var col) && col >= 0)
            return col;
        if (int.TryParse(pair.RowIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out row) && row >= 0)
            return row;
        return null;
    }

    private static double? TryParseDouble(string? value)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue))
            return invariantValue;
        return double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentValue)
            ? currentValue
            : null;
    }
}
