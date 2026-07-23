using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static partial class AstronomyEventTypeNormalizer
{
    public static string Normalize(string eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var normalized = SeparatorRegex().Replace(eventType.Trim(), "_");
        normalized = PascalCaseBoundaryRegex().Replace(normalized, "_");
        normalized = AcronymBoundaryRegex().Replace(normalized, "_");
        normalized = RepeatedUnderscoreRegex().Replace(normalized, "_").Trim('_');

        return normalized.ToUpperInvariant();
    }

    [GeneratedRegex("[-\\s]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex PascalCaseBoundaryRegex();

    [GeneratedRegex("(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex AcronymBoundaryRegex();

    [GeneratedRegex("_+")]
    private static partial Regex RepeatedUnderscoreRegex();
}
