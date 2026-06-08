namespace RRDA.Web.Areas.Data.Controllers;

public sealed class TypePivotRangeDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public List<string> ExpandedHeaders { get; set; } = [];
}

public sealed class TypePivotRangePoint
{
    public int Index { get; set; }
    public double Value { get; set; }
}

public sealed class TypePivotRangeCell
{
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public List<TypePivotRangePoint> Points { get; set; } = [];
    public double Min { get; set; }
    public int MinIndex { get; set; }
    public double Max { get; set; }
    public int MaxIndex { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double StandardDeviation { get; set; }
}
