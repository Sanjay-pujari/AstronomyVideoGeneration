using System.Globalization;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries;

/// <summary>Transforms contracted facts into reusable documentary writing guidance without reading raw metadata.</summary>
public sealed class DocumentaryFactTransformer
{
    /// <summary>Transforms a narration fact into natural documentary phrasing guidance.</summary>
    public string Transform(NarrationFactV5 fact)
    {
        var name = fact.Name;
        var value = fact.Value;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (name.Contains("date", StringComparison.OrdinalIgnoreCase)) return $"Introduce the timing conversationally, for example: {FormatDate(value)}.";
        if (name.Contains("relative", StringComparison.OrdinalIgnoreCase) || name.Contains("separation", StringComparison.OrdinalIgnoreCase)) return $"Describe angular spacing approximately: {FormatDegrees(value)}.";
        if (name.Contains("window", StringComparison.OrdinalIgnoreCase)) return "Describe the best opportunity in ordinary viewing language.";
        if (name.Contains("direction", StringComparison.OrdinalIgnoreCase)) return "Describe where to look as horizon-oriented guidance.";
        if (name.Contains("visibility", StringComparison.OrdinalIgnoreCase) || name.Contains("region", StringComparison.OrdinalIgnoreCase)) return "Describe visibility as selected-region availability.";
        return $"Weave {Humanize(name)} into a spoken sentence only if it supports the scene.";
    }

    private static string FormatDate(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? $"On the evening of {date:MMMM d}" : "on the stated observing date";
    private static string FormatDegrees(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var degrees) ? $"about {ToFriendlyNumber(degrees)} degrees apart" : "about the stated separation apart";
    private static string ToFriendlyNumber(decimal value) => value switch { >= 1.45m and <= 1.75m => "one and a half", _ => value.ToString("0.#", CultureInfo.InvariantCulture) };
    private static string Humanize(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : i == 0 ? char.ToUpperInvariant(c).ToString() : c.ToString()));
}
