using System.Globalization;
using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public static class NarrationTimeFormatter
{
    public static string FormatEventDate(string? eventDate)
        => DateTime.TryParseExact(Clean(eventDate), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
            : RemoveTechnicalArtifacts(Clean(eventDate));

    public static string FormatPeakTime(string? localPeakTime, string fallbackTimeZone = "")
    {
        var cleaned = Clean(localPeakTime);
        var match = Regex.Match(cleaned, @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{1,2}:\d{2})(?:\s+(?<offset>[+-]\d{2}:\d{2}))?(?:\s+(?<tz>[A-Z]{2,5}))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return RemoveTechnicalArtifacts(cleaned);
        var timeZone = !string.IsNullOrWhiteSpace(match.Groups["tz"].Value) ? match.Groups["tz"].Value.ToUpperInvariant() : fallbackTimeZone;
        return Clean($"{FormatClock(match.Groups["time"].Value)} {timeZone}");
    }

    public static string FormatViewingWindow(string? bestViewingWindowLocal, string fallbackTimeZone = "")
    {
        var cleaned = Clean(bestViewingWindowLocal);
        var match = Regex.Match(cleaned, @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<start>\d{1,2}:\d{2})\s*[–-]\s*(?<end>\d{1,2}:\d{2})\s*(?<tz>[A-Z]{2,5})?$", RegexOptions.IgnoreCase);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return RemoveTechnicalArtifacts(cleaned);
        var timeZone = !string.IsNullOrWhiteSpace(match.Groups["tz"].Value) ? match.Groups["tz"].Value.ToUpperInvariant() : fallbackTimeZone;
        return Clean($"from {FormatClock(match.Groups["start"].Value)} to {FormatClock(match.Groups["end"].Value)} {timeZone} on {date:MMMM d, yyyy}");
    }

    public static string FormatDirection(string? direction)
    {
        var cleaned = Clean(direction).ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"^look\s+", string.Empty, RegexOptions.IgnoreCase).Replace(" direction", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (cleaned.Contains("east") && cleaned.Contains("overhead")) return AppendAfterCue(cleaned, "from the eastern sky toward overhead");
        if (cleaned.Contains("north") && cleaned.Contains("overhead")) return AppendAfterCue(cleaned, "from the northern sky toward overhead");
        if (cleaned.Contains("south") && cleaned.Contains("overhead")) return AppendAfterCue(cleaned, "from the southern sky toward overhead");
        if (cleaned.Contains("west") && cleaned.Contains("overhead")) return AppendAfterCue(cleaned, "from the western sky toward overhead");
        if (cleaned.Contains("east")) return AppendAfterCue(cleaned, "toward the eastern sky");
        if (cleaned.Contains("west")) return AppendAfterCue(cleaned, "toward the western sky");
        if (cleaned.Contains("north")) return AppendAfterCue(cleaned, "toward the northern sky");
        if (cleaned.Contains("south")) return AppendAfterCue(cleaned, "toward the southern sky");
        return "across the darkest open sky";
    }

    private static string AppendAfterCue(string source, string formatted)
    {
        var match = Regex.Match(source, @"\bafter\s+\d{1,2}(?::\d{2})?\s*(?:am|pm)\b", RegexOptions.IgnoreCase);
        return match.Success ? $"{formatted} {FormatAfterCue(match.Value)}" : formatted;
    }

    private static string FormatAfterCue(string value)
        => Regex.Replace(value.ToLowerInvariant(), @"\b(am|pm)\b", m => m.Value.ToUpperInvariant(), RegexOptions.IgnoreCase);

    private static string FormatClock(string value)
    {
        if (!TimeOnly.TryParseExact(value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            && !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) return value;
        if (time.Hour == 0 && time.Minute == 0) return "midnight";
        if (time.Hour == 12 && time.Minute == 0) return "noon";
        return DateTime.Today.Add(time.ToTimeSpan()).ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    private static string RemoveTechnicalArtifacts(string value)
    {
        var cleaned = Regex.Replace(value, @"\b\d{4}-\d{2}-\d{2}\b", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?<!\w)[+-]\d{2}:\d{2}(?!\w)", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\b(?:listed|local) viewing window\b", "recommended viewing window", RegexOptions.IgnoreCase);
        return Clean(cleaned.Trim(' ', ',', '-', '–', '—'));
    }

    private static string Clean(string? text) => Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim();
}
