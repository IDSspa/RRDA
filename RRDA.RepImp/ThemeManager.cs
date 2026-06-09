using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace RRDA.RepImp;

internal static class ThemeManager
{
    public const string SystemTheme = "system";
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    public static void Apply(string? preference)
    {
        var theme = Normalize(preference);
        var useDarkTheme = theme == DarkTheme || theme == SystemTheme && IsSystemDarkTheme();
        var colors = useDarkTheme ? DarkColors : LightColors;

        foreach (var (resourceKey, color) in colors)
        {
            Application.Current.Resources[resourceKey] = new SolidColorBrush(color);
        }
    }

    public static string Normalize(string? preference) =>
        preference?.ToLowerInvariant() switch
        {
            LightTheme => LightTheme,
            DarkTheme => DarkTheme,
            _ => SystemTheme
        };

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static readonly IReadOnlyDictionary<string, Color> LightColors =
        new Dictionary<string, Color>
        {
            ["WindowBackgroundBrush"] = Color.FromRgb(0xF2, 0xF4, 0xF5),
            ["SurfaceBrush"] = Colors.White,
            ["SurfaceMutedBrush"] = Color.FromRgb(0xE7, 0xEC, 0xEE),
            ["BorderBrush"] = Color.FromRgb(0xC8, 0xD0, 0xD3),
            ["TextBrush"] = Color.FromRgb(0x25, 0x32, 0x38),
            ["MutedTextBrush"] = Color.FromRgb(0x5F, 0x6F, 0x75),
            ["AccentBrush"] = Color.FromRgb(0x08, 0x7F, 0x8C),
            ["AccentHoverBrush"] = Color.FromRgb(0x06, 0x68, 0x73),
            ["SelectionBrush"] = Color.FromRgb(0xD9, 0xEE, 0xF0),
            ["ButtonHoverBrush"] = Color.FromRgb(0xDC, 0xE4, 0xE6),
            ["ButtonHoverBorderBrush"] = Color.FromRgb(0x9E, 0xAD, 0xB2),
            ["HeaderBrush"] = Color.FromRgb(0xDC, 0xE3, 0xE5),
            ["HeaderHoverBrush"] = Color.FromRgb(0xCF, 0xDA, 0xDD)
        };

    private static readonly IReadOnlyDictionary<string, Color> DarkColors =
        new Dictionary<string, Color>
        {
            ["WindowBackgroundBrush"] = Color.FromRgb(0x16, 0x1B, 0x1D),
            ["SurfaceBrush"] = Color.FromRgb(0x20, 0x27, 0x2A),
            ["SurfaceMutedBrush"] = Color.FromRgb(0x2B, 0x34, 0x38),
            ["BorderBrush"] = Color.FromRgb(0x45, 0x52, 0x57),
            ["TextBrush"] = Color.FromRgb(0xE5, 0xEC, 0xEE),
            ["MutedTextBrush"] = Color.FromRgb(0xA8, 0xB5, 0xB9),
            ["AccentBrush"] = Color.FromRgb(0x20, 0xB2, 0xAA),
            ["AccentHoverBrush"] = Color.FromRgb(0x36, 0xC5, 0xBC),
            ["SelectionBrush"] = Color.FromRgb(0x16, 0x4D, 0x50),
            ["ButtonHoverBrush"] = Color.FromRgb(0x36, 0x42, 0x46),
            ["ButtonHoverBorderBrush"] = Color.FromRgb(0x68, 0x78, 0x7D),
            ["HeaderBrush"] = Color.FromRgb(0x2B, 0x34, 0x38),
            ["HeaderHoverBrush"] = Color.FromRgb(0x36, 0x42, 0x46)
        };
}
