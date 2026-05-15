using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailCtrScoringService : IThumbnailCtrScoringService
{
    private static readonly string[] GenericHooks = ["visible tonight", "sky event", "astronomy update", "आज रात खगोल विज्ञान घटना"];
    private static readonly string[] AstronomyKeywords = ["moon", "चांद", "jupiter", "बृहस्पति", "saturn", "शनि", "venus", "शुक्र", "mars", "मंगल", "mercury", "बुध", "meteor", "उल्का", "eclipse", "ग्रहण", "comet", "धूमकेतु", "sky", "आकाश", "sunset", "सूर्यास्त", "west", "पश्चिम", "tonight", "आज", "rare", "दुर्लभ"];
    private readonly ThumbnailAIOptimizationOptions _options;

    public ThumbnailCtrScoringService()
        : this(Options.Create(new ThumbnailAIOptimizationOptions()))
    {
    }

    public ThumbnailCtrScoringService(IOptions<ThumbnailAIOptimizationOptions> options)
    {
        _options = options.Value;
    }

    public ThumbnailHookScore Score(string hook, ThumbnailAiOptimizationRequest request)
    {
        var normalized = Normalize(hook);
        var words = CountWords(normalized);
        var emotion = DetectEmotion(normalized);
        var readability = ScoreReadability(normalized, words);
        var astronomyAccuracy = ScoreAstronomyAccuracy(normalized, request);
        var rejectionReason = BuildRejectionReason(normalized, words, readability, astronomyAccuracy);

        var brevity = words <= 3 ? 1.0 : words <= _options.MaxHookWords ? 0.86 : 0.0;
        var emotionalStrength = ScoreEmotion(normalized, emotion);
        var mobileVisibility = normalized.Length <= 24 ? 1.0 : normalized.Length <= 32 ? 0.82 : 0.55;
        var keywordPower = ScoreKeywordPower(normalized, request);
        var analyticsSimilarity = ScoreAnalyticsSimilarity(normalized, request.TopPerformingHooks);
        var rawScore =
            (readability * 0.18) +
            (emotionalStrength * 0.18) +
            (brevity * 0.14) +
            (astronomyAccuracy * 0.22) +
            (mobileVisibility * 0.10) +
            (keywordPower * 0.10) +
            (analyticsSimilarity * 0.08);

        if (!string.IsNullOrWhiteSpace(rejectionReason))
            rawScore = Math.Min(rawScore, _options.MinimumConfidence - 0.05);

        return new ThumbnailHookScore
        {
            Hook = normalized,
            Score = Math.Round(Math.Clamp(rawScore, 0, 1), 3),
            EmotionType = emotion,
            Readability = Math.Round(readability, 3),
            AstronomyAccuracy = Math.Round(astronomyAccuracy, 3),
            RejectionReason = rejectionReason
        };
    }

    private string? BuildRejectionReason(string hook, int words, double readability, double astronomyAccuracy)
    {
        if (string.IsNullOrWhiteSpace(hook))
            return "empty hook";
        if (words > _options.MaxHookWords)
            return $"exceeds {_options.MaxHookWords} words";
        if (_options.DisallowedPatterns.Any(pattern => ContainsToken(hook, pattern)))
            return "contains disallowed pattern";
        if (_options.PreventScientificHallucinations && ContainsHallucination(hook))
            return "potential astronomy hallucination";
        if (readability < 0.45)
            return "low readability";
        if (GenericHooks.Any(x => hook.Equals(x, StringComparison.OrdinalIgnoreCase)))
            return "generic hook";
        if (_options.PreventScientificHallucinations && astronomyAccuracy < 0.55)
            return "low astronomy relevance";
        return null;
    }

    private static double ScoreReadability(string hook, int words)
    {
        if (string.IsNullOrWhiteSpace(hook) || words == 0)
            return 0;

        var averageWordLength = hook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Average(w => w.Length);
        var score = 1.0;
        if (averageWordLength > 10) score -= 0.25;
        if (hook.Length > 32) score -= 0.25;
        if (Regex.IsMatch(hook, @"[!?]{2,}|\d{4,}")) score -= 0.15;
        return Math.Clamp(score, 0, 1);
    }

    private static double ScoreAstronomyAccuracy(string hook, ThumbnailAiOptimizationRequest request)
    {
        if (ContainsHallucination(hook))
            return 0;

        var knownTerms = BuildKnownTerms(request).ToArray();
        if (knownTerms.Any(term => ContainsToken(hook, term)))
            return 1.0;
        if (AstronomyKeywords.Any(term => ContainsToken(hook, term)))
            return 0.82;
        return 0.45;
    }

    private static double ScoreEmotion(string hook, string emotion)
    {
        var baseScore = emotion switch
        {
            "rarity" => 0.95,
            "urgency" => 0.90,
            "curiosity" => 0.86,
            "discovery" => 0.84,
            _ => 0.78
        };

        if (hook.Contains("tonight", StringComparison.OrdinalIgnoreCase) || hook.Contains("आज", StringComparison.OrdinalIgnoreCase))
            baseScore += 0.06;
        return Math.Clamp(baseScore, 0, 1);
    }

    private static double ScoreKeywordPower(string hook, ThumbnailAiOptimizationRequest request)
    {
        var score = 0.45;
        if (BuildKnownTerms(request).Any(term => ContainsToken(hook, term))) score += 0.30;
        if (hook.Contains("tonight", StringComparison.OrdinalIgnoreCase) || hook.Contains("आज", StringComparison.OrdinalIgnoreCase)) score += 0.15;
        if (hook.Contains("rare", StringComparison.OrdinalIgnoreCase) || hook.Contains("दुर्लभ", StringComparison.OrdinalIgnoreCase)) score += 0.10;
        return Math.Clamp(score, 0, 1);
    }

    private static double ScoreAnalyticsSimilarity(string hook, IReadOnlyCollection<string> topHooks)
    {
        if (topHooks.Count == 0)
            return 0.5;

        var hookTokens = Tokenize(hook).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var best = topHooks
            .Select(top => Tokenize(top).ToHashSet(StringComparer.OrdinalIgnoreCase))
            .Where(tokens => tokens.Count > 0)
            .Select(tokens => hookTokens.Intersect(tokens, StringComparer.OrdinalIgnoreCase).Count() / (double)hookTokens.Union(tokens, StringComparer.OrdinalIgnoreCase).Count())
            .DefaultIfEmpty(0)
            .Max();
        return Math.Clamp(0.45 + best, 0, 1);
    }

    private static string DetectEmotion(string hook)
    {
        if (hook.Contains("rare", StringComparison.OrdinalIgnoreCase) || hook.Contains("दुर्लभ", StringComparison.OrdinalIgnoreCase)) return "rarity";
        if (hook.Contains("tonight", StringComparison.OrdinalIgnoreCase) || hook.Contains("आज", StringComparison.OrdinalIgnoreCase)) return "urgency";
        if (hook.Contains("look", StringComparison.OrdinalIgnoreCase) || hook.Contains("देखें", StringComparison.OrdinalIgnoreCase)) return "curiosity";
        if (hook.Contains("meets", StringComparison.OrdinalIgnoreCase) || hook.Contains("और", StringComparison.OrdinalIgnoreCase)) return "discovery";
        return "wonder";
    }

    private static IEnumerable<string> BuildKnownTerms(ThumbnailAiOptimizationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PrimaryObject)) yield return request.PrimaryObject;
        if (!string.IsNullOrWhiteSpace(request.SpecialEvent)) yield return request.SpecialEvent;
        if (!string.IsNullOrWhiteSpace(request.Region)) yield return request.Region;
        foreach (var scene in request.GenerationRequest.Context.SceneObservationContexts)
        {
            if (!string.IsNullOrWhiteSpace(scene.ObjectName)) yield return scene.ObjectName;
            if (!string.IsNullOrWhiteSpace(scene.ObjectType)) yield return scene.ObjectType;
            if (!string.IsNullOrWhiteSpace(scene.DirectionLabel)) yield return scene.DirectionLabel;
        }
        foreach (var ev in request.GenerationRequest.Context.Events)
        {
            if (!string.IsNullOrWhiteSpace(ev.ObjectName)) yield return ev.ObjectName;
            if (!string.IsNullOrWhiteSpace(ev.Direction)) yield return ev.Direction;
        }
    }

    private static bool ContainsHallucination(string hook)
        => new[] { "alien", "ufo", "fake", "apocalypse", "end of earth", "earth ends", "planet x", "nibiru" }
            .Any(pattern => hook.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsToken(string text, string token)
        => !string.IsNullOrWhiteSpace(token) && text.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string hook)
        => string.Join(' ', (hook ?? string.Empty).Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static int CountWords(string hook)
        => hook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private static IEnumerable<string> Tokenize(string text)
        => Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(token => token.Length > 1);
}
