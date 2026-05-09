using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed record LocalizationContext(string RequestedLanguage, string ResolvedLanguage, bool FallbackUsed)
{
    public static LocalizationContext English { get; } = new("en", "en", false);
}

public static class LocalizationResolver
{
    public static LocalizationContext Resolve(string? requestedLanguage, LocalizationOptions? options = null)
    {
        options ??= new LocalizationOptions();
        var requested = Normalize(string.IsNullOrWhiteSpace(requestedLanguage) ? options.DefaultLanguage : requestedLanguage) ?? "en";

        if (!options.Enabled)
        {
            var defaultLanguage = Normalize(options.DefaultLanguage) ?? "en";
            return new LocalizationContext(requested, defaultLanguage, !requested.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase));
        }

        var supported = options.SupportedLanguages
            .Select(Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (supported.Count == 0)
        {
            supported.Add("en");
        }

        if (supported.Contains(requested))
        {
            return new LocalizationContext(requested, requested, false);
        }

        var fallback = Normalize(options.FallbackLanguage) ?? Normalize(options.DefaultLanguage) ?? "en";
        if (!supported.Contains(fallback))
        {
            fallback = supported.Contains("en") ? "en" : supported.First();
        }

        return new LocalizationContext(requested, fallback, true);
    }

    public static string LanguageDisplayName(string? language)
        => Normalize(language) switch
        {
            "hi" => "Hindi (हिन्दी)",
            "en" => "English",
            { } value => value,
            _ => "English"
        };

    public static bool IsHindi(string? language)
        => string.Equals(Normalize(language), "hi", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? language)
        => string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();
}
