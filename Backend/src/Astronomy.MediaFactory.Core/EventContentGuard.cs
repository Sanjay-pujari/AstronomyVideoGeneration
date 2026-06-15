using System.Globalization;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public static class EventContentGuard
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IReadOnlyList<string> DetectForbiddenTerms(string? text, IEnumerable<string>? forbiddenTerms)
    {
        if (string.IsNullOrWhiteSpace(text) || forbiddenTerms is null) return [];
        return forbiddenTerms
            .Where(term => ContainsTerm(text, term))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void ValidateNoForbiddenTerms(string moduleName, string contentArea, string? text, IEnumerable<string>? forbiddenTerms)
    {
        var hits = DetectForbiddenTerms(text, forbiddenTerms);
        if (hits.Count > 0)
            throw new InvalidOperationException($"{moduleName} forbidden terms detected in {contentArea}: {string.Join(", ", hits)}");
    }

    public static void ValidateObject(string moduleName, string contentArea, object value, IEnumerable<string>? forbiddenTerms)
        => ValidateNoForbiddenTerms(moduleName, contentArea, JsonSerializer.Serialize(value, JsonOptions), forbiddenTerms);

    public static EventContentGuardDiagnostics BuildDiagnostics(
        int phaseNo,
        string moduleName,
        string? selectedEventType,
        string? selectedStoryTheme,
        string? selectedVisualTheme,
        IEnumerable<string> sourceFilesUsed,
        string? finalPromptPreview,
        IEnumerable<string>? forbiddenTerms)
    {
        var preview = Truncate(finalPromptPreview ?? string.Empty, 800);
        var detected = DetectForbiddenTerms(finalPromptPreview, forbiddenTerms);
        var golden = DetectForbiddenTerms(finalPromptPreview, GoldenPilotTerms);
        return new EventContentGuardDiagnostics(
            phaseNo,
            moduleName,
            selectedEventType ?? string.Empty,
            selectedStoryTheme ?? string.Empty,
            selectedVisualTheme ?? string.Empty,
            sourceFilesUsed.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            preview,
            detected,
            golden);
    }

    public static IReadOnlyList<string> DefaultForbiddenTermsForEventType(string? eventType)
        => IsPlanetConjunction(eventType) ? GoldenPilotTerms : [];

    public static bool IsPlanetConjunction(string? eventType)
        => !string.IsNullOrWhiteSpace(eventType)
            && (eventType.Contains("CONJUNCTION", StringComparison.OrdinalIgnoreCase)
                || eventType.Contains("PlanetConjunction", StringComparison.OrdinalIgnoreCase)
                || eventType.Contains("PlanetPairing", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsTerm(string text, string? term)
        => !string.IsNullOrWhiteSpace(term)
            && CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase) >= 0;

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static readonly string[] GoldenPilotTerms = ["Geminids", "meteor", "meteor shower", "radiant", "Phaethon", "debris stream", "Dec 13", "Dec 14", "midnight to pre-dawn", "east to overhead"];
}

public sealed record EventContentGuardDiagnostics(
    int PhaseNo,
    string ModuleName,
    string SelectedEventType,
    string SelectedStoryTheme,
    string SelectedVisualTheme,
    IReadOnlyList<string> SourceFilesUsed,
    string FinalPromptPreview,
    IReadOnlyList<string> ForbiddenTermsDetected,
    IReadOnlyList<string> GoldenPilotHardcodingDetected);
