using System.IO;

namespace RRDA.RepImp;

internal static class ConfiguredPathResolver
{
    public static string ResolveValidatorsFolder(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IDS",
            "RRDA RepImp",
            "validators");
    }

    public static string? ResolveFile(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
    }
}
