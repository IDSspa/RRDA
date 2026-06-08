using RRDA.Web.Areas.Data.Controllers;
using RRDA.Web.Services.TypePivot;
using Xunit;

namespace RRDA.Web.Tests;

public sealed class TypePivotRangeAggregatorTests
{
    [Fact]
    public void Build_OrdersHorizontalRangeAndCalculatesSummary()
    {
        var pairs = new[]
        {
            Pair("3", "0", "2"),
            Pair("1", "0", "0"),
            Pair("2", "0", "1")
        };

        var result = TypePivotRangeAggregator.Build("Range", "V", pairs);

        Assert.NotNull(result);
        Assert.Equal([0, 1, 2], result.Points.Select(point => point.Index));
        Assert.Equal(1d, result.Min);
        Assert.Equal(0, result.MinIndex);
        Assert.Equal(3d, result.Max);
        Assert.Equal(2, result.MaxIndex);
        Assert.Equal(2d, result.Mean);
        Assert.Equal(2d, result.Median);
        Assert.Equal(1d, result.StandardDeviation);
    }

    [Fact]
    public void Build_UsesRowIndexForVerticalRange()
    {
        var pairs = new[]
        {
            Pair("20", "1", "0"),
            Pair("10", "0", "0")
        };

        var result = TypePivotRangeAggregator.Build("Range", null, pairs);

        Assert.NotNull(result);
        Assert.Equal([0, 1], result.Points.Select(point => point.Index));
        Assert.Equal([10d, 20d], result.Points.Select(point => point.Value));
    }

    private static PivotPair Pair(string value, string row, string col) => new()
    {
        Value = value,
        RowIndex = row,
        ColIndex = col,
        RangeName = "Range"
    };
}
