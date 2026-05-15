namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailHookService : IThumbnailHookService
{
    private static readonly HashSet<string> UnsafeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "death", "dead", "kill", "killed", "blood", "war", "attack", "disaster"
    };

    public string GenerateHook(ThumbnailGenerationRequest request, int maxWords)
    {
        var isHindi = LocalizationResolver.IsHindi(request.Context.Localization.ResolvedLanguage);
        var candidates = BuildCandidates(request, isHindi)
            .Select(x => Sanitize(x, maxWords))
            .Where(x => !string.IsNullOrWhiteSpace(x) && !ContainsUnsafeWord(x))
            .ToArray();

        return candidates.FirstOrDefault() ?? (isHindi ? "आज दिखेगा आसमान" : "Visible Tonight");
    }

    private static IEnumerable<string> BuildCandidates(ThumbnailGenerationRequest request, bool isHindi)
    {
        if (!string.IsNullOrWhiteSpace(request.Metadata.HookLine) && !IsGenericLongTitle(request.Metadata.HookLine))
            yield return request.Metadata.HookLine;

        foreach (var suggestion in request.Metadata.ThumbnailTextSuggestions)
            yield return suggestion;

        if (!string.IsNullOrWhiteSpace(request.Context.SpecialEvent?.EventTitle))
        {
            var eventTitle = request.Context.SpecialEvent.EventTitle;
            if (isHindi)
                yield return ContainsMoon(eventTitle) ? "सबसे बड़ा चांद आज" : "दुर्लभ आकाश घटना";
            else
                yield return ContainsMoon(eventTitle) ? "Biggest Moon Tonight" : "Rare Sky Event";
        }

        var objectName = request.Context.SceneObservationContexts
            .Select(x => x.ObjectName)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("Sky", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(objectName))
        {
            if (isHindi)
            {
                yield return ContainsMoon(objectName) ? "चांद आज दिखेगा" : $"{objectName} आज दिखेगा";
            }
            else
            {
                yield return ContainsMoon(objectName) ? "Moon Tonight" : $"{objectName} Tonight";
            }
        }

        var direction = request.Context.SceneObservationContexts
            .OrderByDescending(x => x.AltitudeDegrees ?? double.MinValue)
            .FirstOrDefault()?.DirectionLabel;
        if (!string.IsNullOrWhiteSpace(direction) && !isHindi)
            yield return $"Look {direction} Tonight";

        yield return isHindi ? "आज दिखेगा आसमान" : "Visible Tonight";
        yield return isHindi ? "दुर्लभ आकाश घटना" : "Rare Sky Event";
    }

    private static string Sanitize(string text, int maxWords)
    {
        var cleaned = new string(text.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ch == '&').ToArray());
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => !w.Any(char.IsDigit))
            .Take(Math.Clamp(maxWords, 2, 5))
            .ToArray();

        return words.Length < 2 ? string.Empty : string.Join(' ', words);
    }

    private static bool IsGenericLongTitle(string text)
        => text.Contains("Astronomy Sky Guide", StringComparison.OrdinalIgnoreCase)
           || text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 8;

    private static bool ContainsMoon(string text)
        => text.Contains("moon", StringComparison.OrdinalIgnoreCase) || text.Contains("चांद", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUnsafeWord(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(UnsafeWords.Contains);
}
