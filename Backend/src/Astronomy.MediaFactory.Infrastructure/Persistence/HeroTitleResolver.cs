using System;
using System.Linq;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static class HeroTitleResolver
{
    public static (string Value, string Source) Resolve(string? language, params (string Source, string? Value)[] candidates)
    {
        var eventSpecificTitleExists = candidates.Any(candidate => !string.Equals(candidate.Source, "hookBlock.text", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Clean(candidate.Value)));
        foreach (var candidate in candidates)
        {
            var value = Clean(candidate.Value);
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (!IsEnglish(language) && string.Equals(candidate.Source, "hookBlock.text", StringComparison.OrdinalIgnoreCase) && eventSpecificTitleExists)
                continue;
            return (value, candidate.Source);
        }
        return (string.Empty, string.Empty);
    }

    private static bool IsEnglish(string? language) => string.IsNullOrWhiteSpace(language) || language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    private static string Clean(string? value) => string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
