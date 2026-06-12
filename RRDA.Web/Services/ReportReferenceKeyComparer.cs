using System.Globalization;
using System.Numerics;

namespace RRDA.Web.Services;

public static class ReportReferenceKeyComparer
{
    public static bool Equals(string? left, string? right)
    {
        var normalizedLeft = left?.Trim();
        var normalizedRight = right?.Trim();
        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
            return true;

        return BigInteger.TryParse(normalizedLeft, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftNumber)
            && BigInteger.TryParse(normalizedRight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightNumber)
            && leftNumber == rightNumber;
    }
}
