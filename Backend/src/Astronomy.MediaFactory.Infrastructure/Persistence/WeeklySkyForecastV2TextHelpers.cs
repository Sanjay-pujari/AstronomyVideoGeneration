using System.Globalization;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static class WeeklySkyForecastV2TextHelpers
{
    public static string FormatCelestialList(IReadOnlyList<string> objects)
    {
        var names = objects.Select(ToDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return "the evening sky";
        if (names.Count == 1) return names[0];
        if (names.Count == 2) return $"{names[0]} and {names[1]}";
        return $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
    }

    private static string ToDisplayName(string code)
    {
        if (code.Equals("MOON", StringComparison.OrdinalIgnoreCase)) return "the Moon";
        var cleaned = code.Replace('_', ' ').ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned);
    }
}
