using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailAiOptimizationService : IThumbnailAiOptimizationService
{
    private readonly IThumbnailCtrScoringService _scoringService;
    private readonly ThumbnailAIOptimizationOptions _options;

    public ThumbnailAiOptimizationService(IThumbnailCtrScoringService scoringService, IOptions<ThumbnailAIOptimizationOptions> options)
    {
        _scoringService = scoringService;
        _options = options.Value;
    }

    public async Task<ThumbnailAiOptimizationResult> OptimizeAsync(ThumbnailAiOptimizationRequest request, CancellationToken cancellationToken)
    {
        var effectiveRequest = NormalizeRequest(request);
        var candidates = BuildCandidates(effectiveRequest)
            .Select(NormalizeDirectionalHook)
            .Select(hook => LimitWords(hook, _options.MaxHookWords))
            .Where(hook => !string.IsNullOrWhiteSpace(hook) && !IsWeakGenericHook(hook) && !HasRepetitiveWords(hook))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        var scores = candidates
            .Select(hook => _options.EnableCTRScoring ? _scoringService.Score(hook, effectiveRequest) : BuildUnscoredSafeHook(hook))
            .OrderByDescending(score => score.Score)
            .ToArray();

        var selected = scores
            .Where(score => !score.IsRejected && score.Score >= _options.MinimumConfidence)
            .OrderByDescending(score => score.Score)
            .FirstOrDefault()
            ?? scores.FirstOrDefault(score => !score.IsRejected)
            ?? BuildFallbackScore(effectiveRequest);

        var result = new ThumbnailAiOptimizationResult
        {
            CandidateHooks = candidates,
            Scores = scores,
            SelectedHook = selected.Hook,
            NormalizedHook = NormalizeDirectionalHook(selected.Hook),
            CtrScore = selected.Score,
            RejectedHooks = scores.Where(score => score.IsRejected).Select(score => score.Hook).ToArray(),
            EmotionType = selected.EmotionType,
            Language = effectiveRequest.Language ?? effectiveRequest.GenerationRequest.Context.Localization.ResolvedLanguage,
            AnalyticsInfluence = CalculateAnalyticsInfluence(selected.Hook, effectiveRequest.TopPerformingHooks),
            HallucinationDetected = scores.Any(score => score.RejectionReason?.Contains("hallucination", StringComparison.OrdinalIgnoreCase) == true || score.RejectionReason?.Contains("disallowed", StringComparison.OrdinalIgnoreCase) == true),
            VisualPolishPassApplied = true
        };

        await WriteDiagnosticsAsync(effectiveRequest.GenerationRequest.OutputDirectory, result, cancellationToken);
        return result;
    }

    private ThumbnailAiOptimizationRequest NormalizeRequest(ThumbnailAiOptimizationRequest request)
    {
        var generationRequest = request.GenerationRequest;
        var primaryObject = FirstNonEmpty(
            request.PrimaryObject,
            generationRequest.Context.SceneObservationContexts.Select(x => x.ObjectName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("Sky", StringComparison.OrdinalIgnoreCase)),
            generationRequest.Context.Events.OrderByDescending(x => x.Score).Select(x => x.ObjectName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));

        var topHooks = request.TopPerformingHooks
            .Concat(generationRequest.FeedbackSignals?.BestHooks ?? [])
            .Concat(generationRequest.Context.PromptFeedbackContext?.ShortsHookSuggestions ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        return new ThumbnailAiOptimizationRequest
        {
            GenerationRequest = generationRequest,
            PrimaryObject = primaryObject,
            SpecialEvent = FirstNonEmpty(request.SpecialEvent, generationRequest.Context.SpecialEvent?.EventTitle),
            Region = FirstNonEmpty(request.Region, generationRequest.Context.LocationName),
            Language = FirstNonEmpty(request.Language, generationRequest.Context.Localization.ResolvedLanguage),
            SeoTitle = FirstNonEmpty(request.SeoTitle, generationRequest.Metadata.PrimaryTitle),
            TopPerformingHooks = topHooks
        };
    }

    private IEnumerable<string> BuildCandidates(ThumbnailAiOptimizationRequest request)
    {
        if (!_options.Enabled || !_options.EnableHookOptimization)
        {
            yield return request.GenerationRequest.Metadata.HookLine ?? FallbackHook(request);
            yield break;
        }

        var isHindi = LocalizationResolver.IsHindi(request.Language ?? request.GenerationRequest.Context.Localization.ResolvedLanguage);
        foreach (var metadataHook in BuildMetadataHooks(request.GenerationRequest))
            yield return metadataHook;

        var primaryObject = request.PrimaryObject;
        var eventTitle = request.SpecialEvent;
        var direction = request.GenerationRequest.Context.SceneObservationContexts
            .OrderByDescending(x => x.AltitudeDegrees ?? double.MinValue)
            .FirstOrDefault()?.DirectionLabel;

        if (isHindi)
        {
            if (MentionsMoon(primaryObject) && MentionsJupiter(eventTitle)) yield return "चांद और बृहस्पति";
            if (!string.IsNullOrWhiteSpace(eventTitle)) yield return MentionsMoon(eventTitle) ? "सबसे बड़ा चांद आज" : "दुर्लभ घटना";
            if (!string.IsNullOrWhiteSpace(direction) && direction.Equals("West", StringComparison.OrdinalIgnoreCase)) yield return "पश्चिम में देखें";
            yield return "आज रात देखें";
            if (MentionsJupiter(primaryObject)) yield return "बृहस्पति आज दिखेगा";
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(eventTitle)) yield return MentionsMoon(eventTitle) ? "Biggest Moon Tonight" : "Rare Sky Event";
            if (MentionsMoon(primaryObject) && MentionsJupiter(eventTitle)) yield return "Moon Meets Jupiter";
            if (!string.IsNullOrWhiteSpace(primaryObject)) yield return $"{primaryObject} Tonight";
            if (!string.IsNullOrWhiteSpace(direction)) yield return $"Look {direction} Tonight";
            yield return "Venus After Sunset";
        }

        foreach (var hook in request.TopPerformingHooks)
            yield return hook;

        yield return FallbackHook(request);
    }

    private static IEnumerable<string> BuildMetadataHooks(ThumbnailGenerationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Metadata.HookLine)) yield return request.Metadata.HookLine;
        foreach (var suggestion in request.Metadata.ThumbnailTextSuggestions) yield return suggestion;
    }

    private ThumbnailHookScore BuildUnscoredSafeHook(string hook) => new()
    {
        Hook = hook,
        Score = Math.Max(_options.MinimumConfidence, 0.70),
        EmotionType = "wonder",
        Readability = 1,
        AstronomyAccuracy = 1
    };

    private static ThumbnailHookScore BuildFallbackScore(ThumbnailAiOptimizationRequest request)
    {
        var hook = FallbackHook(request);
        return new ThumbnailHookScore
        {
            Hook = hook,
            Score = 0.70,
            EmotionType = LocalizationResolver.IsHindi(request.Language ?? string.Empty) ? "urgency" : "wonder",
            Readability = 0.80,
            AstronomyAccuracy = 0.80
        };
    }

    private async Task WriteDiagnosticsAsync(string outputDirectory, ThumbnailAiOptimizationResult result, CancellationToken cancellationToken)
    {
        var thumbnailsDirectory = Path.Combine(outputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);
        var path = Path.Combine(thumbnailsDirectory, _options.OutputFileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken);
    }

    private static double CalculateAnalyticsInfluence(string selectedHook, IReadOnlyCollection<string> topHooks)
    {
        if (topHooks.Count == 0)
            return 0;
        var selectedTokens = selectedHook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedTokens.Count == 0)
            return 0;
        return Math.Round(topHooks.Max(hook => selectedTokens.Intersect(hook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase).Count() / (double)selectedTokens.Count), 3);
    }

    private static string FallbackHook(ThumbnailAiOptimizationRequest request)
        => LocalizationResolver.IsHindi(request.Language ?? string.Empty) ? "आज रात देखें" : "Look West Tonight";

    public static string NormalizeDirectionalHook(string text)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Look W Tonight"] = "Look West Tonight",
            ["Look E Tonight"] = "Look East Tonight",
            ["Look N Tonight"] = "Look North Tonight",
            ["Look S Tonight"] = "Look South Tonight"
        };
        return replacements.TryGetValue(normalized, out var expanded) ? expanded : normalized;
    }

    private string LimitWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(Math.Clamp(maxWords, 1, Math.Max(1, _options.MaxHookWords)))
            .ToArray();
        return string.Join(' ', words);
    }

    private static bool MentionsMoon(string? value) => value?.Contains("moon", StringComparison.OrdinalIgnoreCase) == true || value?.Contains("चांद", StringComparison.OrdinalIgnoreCase) == true;
    private static bool MentionsJupiter(string? value) => value?.Contains("jupiter", StringComparison.OrdinalIgnoreCase) == true || value?.Contains("बृहस्पति", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsWeakGenericHook(string hook)
    {
        var normalized = string.Join(' ', hook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Equals("Visible Tonight", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("Tonight's Sky", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Beginner", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Guide", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRepetitiveWords(string hook)
    {
        var words = hook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
