using System.Globalization;
using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public static class HeroMetadataNormalizer
{
    public static string NormalizeTime(string? rawTime, string? eventFamily, string? language)
    {
        var clean = Clean(rawTime);
        if (IsSolarEclipse(eventFamily) && IsMissing(clean)) return "MAX ECLIPSE";
        if (string.IsNullOrWhiteSpace(clean)) return IsSolarEclipse(eventFamily) ? "MAX ECLIPSE" : "VIEWING WINDOW";

        var ampm = Regex.Match(clean, @"\b(?<time>\d{1,2}:\d{2}\s*(?:AM|PM)(?:\s+[A-Z]{2,5})?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (ampm.Success) return Clean(ampm.Groups["time"].Value).ToUpperInvariant();

        var iso = Regex.Match(clean, @"\b(?<date>\d{4}-\d{2}-\d{2})[T\s]+(?<hour>\d{1,2}):(?<minute>\d{2})(?::\d{2})?\s*(?<offset>Z|[+-]\d{2}:?\d{2})?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (iso.Success)
        {
            var offset = iso.Groups["offset"].Success ? iso.Groups["offset"].Value : "+00:00";
            if (offset.Length == 5 && (offset[0] == '+' || offset[0] == '-')) offset = offset.Insert(3, ":");
            if (DateTimeOffset.TryParse($"{iso.Groups["date"].Value}T{iso.Groups["hour"].Value.PadLeft(2, '0')}:{iso.Groups["minute"].Value}:00{offset}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                var ist = dto.ToOffset(TimeSpan.FromHours(5.5));
                return ist.ToString("h:mm tt", CultureInfo.InvariantCulture).ToUpperInvariant() + " IST";
            }
        }

        var hourMinute = Regex.Match(clean, @"\b(?<hour>\d{1,2}):(?<minute>\d{2})\b", RegexOptions.CultureInvariant);
        if (hourMinute.Success) return hourMinute.Value;

        return IsSolarEclipse(eventFamily) ? "MAX ECLIPSE" : TrimSentence(clean, 4).ToUpperInvariant();
    }

    public static string NormalizeDirection(string? rawDirection, string? eventFamily, string? language)
    {
        var clean = Clean(rawDirection);
        if (IsSolarEclipse(eventFamily)) return "SAFE SOLAR VIEWING";
        if (ContainsDirection(clean, "northeast", "north east", "NE")) return "NORTHEAST";
        if (ContainsDirection(clean, "southeast", "south east", "southeastern", "SE")) return "SOUTHEAST";
        if (ContainsDirection(clean, "east", "eastern")) return IsPlanetPairing(eventFamily) ? "EAST" : "EASTERN SKY";
        if (ContainsDirection(clean, "northwest", "north west", "NW")) return "NORTHWEST";
        if (ContainsDirection(clean, "southwest", "south west", "SW")) return "SOUTHWEST";
        if (ContainsDirection(clean, "west", "western")) return IsPlanetPairing(eventFamily) ? "WEST" : "WESTERN SKY";
        if (ContainsDirection(clean, "north", "northern")) return "NORTH";
        if (ContainsDirection(clean, "south", "southern")) return "SOUTH";
        if (clean.Contains("moonrise", StringComparison.OrdinalIgnoreCase)) return "EASTERN SKY";
        if (clean.Contains("midnight", StringComparison.OrdinalIgnoreCase)) return "AFTER MIDNIGHT";
        return "BEST VIEWING SKY";
    }

    public static string NormalizeTitle(string? rawTitle, string? eventFamily, string? language)
    {
        var clean = StripRawRegionIds(Clean(rawTitle)).Trim(' ', ',', '.', '-', '–');
        if (IsSolarEclipse(eventFamily)) return clean.Contains("total", StringComparison.OrdinalIgnoreCase) ? "TOTAL SOLAR ECLIPSE" : "SOLAR ECLIPSE";
        if (string.IsNullOrWhiteSpace(clean)) clean = "SKY EVENT";
        return TrimTitle(clean).ToUpperInvariant();
    }

    public static string NormalizeSubtitle(IEnumerable<string>? primaryObjects, string? shortTitle, string? eventFamily, string? language)
    {
        if (IsSolarEclipse(eventFamily)) return "SUN + MOON";
        if (IsMeteor(eventFamily)) return string.IsNullOrWhiteSpace(shortTitle) ? "METEOR SHOWER" : NormalizeTitle(shortTitle, eventFamily, language);
        if (IsFullMoon(eventFamily)) return "FULL MOON";
        var objects = (primaryObjects ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Clean(value).ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (objects.Length is >= 2 and <= 4) return string.Join(" + ", objects);
        if (objects.Length >= 5) return string.Join(" + ", objects.Take(3).Concat([$"{objects.Length - 3} MORE"]));
        return string.IsNullOrWhiteSpace(shortTitle) ? "SKY EVENT" : NormalizeTitle(shortTitle, eventFamily, language);
    }

    private static bool ContainsDirection(string value, params string[] terms) => terms.Any(term => Regex.IsMatch(value, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    private static bool IsSolarEclipse(string? v) => Contains(v, "solar") && Contains(v, "eclipse");
    private static bool IsMeteor(string? v) => Contains(v, "meteor");
    private static bool IsFullMoon(string? v) => Contains(v, "moon") && (Contains(v, "full") || Contains(v, "wolf") || Contains(v, "named"));
    private static bool IsPlanetPairing(string? v) => Contains(v, "pair") || Contains(v, "conjunction");
    private static bool Contains(string? v, string term) => (v ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase);
    private static bool IsMissing(string v) => string.IsNullOrWhiteSpace(v) || v.Contains("unknown", StringComparison.OrdinalIgnoreCase) || v.Contains("tbd", StringComparison.OrdinalIgnoreCase) || v.Contains("missing", StringComparison.OrdinalIgnoreCase);
    private static string Clean(string? value) => string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static string StripRawRegionIds(string value) => Regex.Replace(value, @"\b[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+\b", "", RegexOptions.CultureInvariant);
    private static string TrimSentence(string value, int maxWords) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(maxWords)).Trim(' ', ',', '.', '-', '–');
    private static string TrimTitle(string value) => value.Length <= 56 ? value : value[..56].TrimEnd(' ', ',', '-', '–', ':');
}
