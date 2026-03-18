using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core;

public interface IMetadataOptimizationModelClient
{
    Task<OptimizedVideoMetadata?> TryOptimizeAsync(MetadataOptimizationInput input, bool isShort, CancellationToken cancellationToken);
}

public sealed class MetadataOptimizationService : IMetadataOptimizationService
{
    private const int MaxTags = 15;
    private const int MaxHashtags = 8;

    private static readonly IReadOnlyDictionary<ContentType, ContentTypeMetadataStrategy> Strategies =
        new Dictionary<ContentType, ContentTypeMetadataStrategy>
        {
            [ContentType.DailySkyGuide] = new(
                LongFormTitle: (lead, _, datePart) => lead is null ? $"Tonight's Sky Guide ({datePart})" : $"Tonight's Sky: {lead} & More ({datePart})",
                ShortFormTitle: (lead, _, _) => lead is null ? "Tonight's Sky in 60 Seconds" : $"{lead} in 60 Seconds",
                AlternateCategoryLabel: "Night Sky",
                ContentTypeHashtag: "#tonightsky",
                LongThumbnailLabel: "EASY SKY GUIDE"),

            [ContentType.TelescopeTargets] = new(
                LongFormTitle: (lead, _, _) => lead is null ? "Best Telescope Targets for Beginners Tonight" : $"Beginner Telescope Target: {lead} Tonight",
                ShortFormTitle: (lead, _, _) => lead is null ? "Quick Telescope Target" : $"Find {lead} Fast",
                AlternateCategoryLabel: "Telescope",
                ContentTypeHashtag: "#telescope",
                LongThumbnailLabel: "BEGINNER TARGET"),

            [ContentType.SpaceNews] = new(
                LongFormTitle: (lead, sourceTitle, _) => lead is null ? CleanTitle(sourceTitle, isShort: false) : $"Space Update: {lead} Explained",
                ShortFormTitle: (lead, sourceTitle, _) => lead is null ? CleanTitle(sourceTitle, isShort: true) : $"Quick Space Fact: {lead}",
                AlternateCategoryLabel: "Space Update",
                ContentTypeHashtag: "#spacenews",
                LongThumbnailLabel: "NEW SPACE UPDATE"),

            [ContentType.AstrophotographyTips] = new(
                LongFormTitle: (lead, _, _) => lead is null ? "Astrophotography Tips for Beginners Tonight" : $"Photograph {lead}: Beginner Camera Settings",
                ShortFormTitle: (lead, _, _) => lead is null ? "Fast Astrophotography Tip" : $"Shoot {lead} Better Tonight",
                AlternateCategoryLabel: "Astrophotography",
                ContentTypeHashtag: "#astrophotography",
                LongThumbnailLabel: "CAMERA SETTINGS")
        };

    private readonly IMetadataOptimizationModelClient? _modelClient;
    private readonly ILogger<MetadataOptimizationService> _logger;

    public MetadataOptimizationService(ILogger<MetadataOptimizationService> logger, IMetadataOptimizationModelClient? modelClient = null)
    {
        _logger = logger;
        _modelClient = modelClient;
    }

    public Task<OptimizedVideoMetadata> OptimizeForVideoAsync(MetadataOptimizationInput input, CancellationToken cancellationToken)
        => OptimizeAsync(input, isShort: false, cancellationToken);

    public Task<OptimizedVideoMetadata> OptimizeForShortAsync(MetadataOptimizationInput input, CancellationToken cancellationToken)
        => OptimizeAsync(input, isShort: true, cancellationToken);

    private async Task<OptimizedVideoMetadata> OptimizeAsync(MetadataOptimizationInput input, bool isShort, CancellationToken cancellationToken)
    {
        ValidateInput(input);

        if (_modelClient is not null)
        {
            try
            {
                var aiResult = await _modelClient.TryOptimizeAsync(input, isShort, cancellationToken);
                if (TryNormalizeModelResult(aiResult, input.ContentType, isShort, out var normalized))
                {
                    return normalized;
                }

                _logger.LogWarning("AI metadata optimization returned empty/invalid payload for {ContentForm}. Falling back to deterministic strategy.", isShort ? "short" : "long-form video");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI metadata optimization failed for {ContentForm}. Falling back to deterministic strategy.", isShort ? "short" : "long-form video");
            }
        }

        return BuildDeterministic(input, isShort);
    }

    private static void ValidateInput(MetadataOptimizationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Context);

        if (string.IsNullOrWhiteSpace(input.SourceTitle))
        {
            throw new ArgumentException("SourceTitle is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.SourceDescription))
        {
            throw new ArgumentException("SourceDescription is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.Context.LocationName))
        {
            throw new ArgumentException("Context.LocationName is required.", nameof(input));
        }

        if (input.SourceTags is null)
        {
            throw new ArgumentException("SourceTags cannot be null.", nameof(input));
        }
    }

    private static bool TryNormalizeModelResult(OptimizedVideoMetadata? modelResult, ContentType type, bool isShort, out OptimizedVideoMetadata normalized)
    {
        if (modelResult is null)
        {
            normalized = null!;
            return false;
        }

        normalized = Normalize(modelResult, type, isShort);
        return IsUsable(normalized);
    }

    private static bool IsUsable(OptimizedVideoMetadata metadata)
        => !string.IsNullOrWhiteSpace(metadata.PrimaryTitle)
           && !string.IsNullOrWhiteSpace(metadata.OptimizedDescription)
           && metadata.Tags.Length > 0
           && metadata.Hashtags.Length > 0;

    private static OptimizedVideoMetadata BuildDeterministic(MetadataOptimizationInput input, bool isShort)
    {
        var strategy = ResolveStrategy(input.ContentType);
        var topObjects = input.Context.Events
            .OrderByDescending(e => e.Score)
            .Take(3)
            .Select(e => e.ObjectName)
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var datePart = input.Context.Date.ToString("MMM dd");
        var hashtags = BuildHashtags(strategy, topObjects, isShort);

        return new OptimizedVideoMetadata
        {
            PrimaryTitle = BuildTitle(strategy, input.SourceTitle, topObjects, datePart, isShort),
            AlternateTitles = BuildAlternates(strategy, input, topObjects, datePart, isShort),
            OptimizedDescription = BuildDescription(input, hashtags, isShort),
            Tags = BuildTags(input.ContentType, input.SourceTags, input.FeedbackKeywords, topObjects, isShort),
            Hashtags = hashtags,
            ThumbnailTextSuggestions = BuildThumbnailSuggestions(strategy, topObjects, isShort),
            HookLine = isShort
                ? (!string.IsNullOrWhiteSpace(input.SourceHookLine)
                    ? input.SourceHookLine.Trim()
                    : $"Tonight's quick sky highlight: {topObjects.FirstOrDefault() ?? "look up after sunset"}.")
                : null
        };
    }

    private static string BuildTitle(ContentTypeMetadataStrategy strategy, string sourceTitle, string[] topObjects, string datePart, bool isShort)
    {
        var lead = topObjects.FirstOrDefault();
        var raw = isShort
            ? strategy.ShortFormTitle(lead, sourceTitle, datePart)
            : strategy.LongFormTitle(lead, sourceTitle, datePart);

        return CleanTitle(raw, isShort);
    }

    private static string[] BuildAlternates(ContentTypeMetadataStrategy strategy, MetadataOptimizationInput input, string[] topObjects, string datePart, bool isShort)
    {
        var lead = topObjects.FirstOrDefault() ?? strategy.AlternateCategoryLabel;
        var items = new List<string>
        {
            isShort ? $"{lead} in 60 Seconds" : $"{lead} Sky Guide for {datePart}",
            isShort ? $"Quick Space Fact: {lead}" : $"What to Watch Tonight: {lead}",
            isShort ? "Look Up Tonight #shorts" : $"Easy {strategy.AlternateCategoryLabel} Astronomy Guide"
        };

        foreach (var pattern in input.FeedbackContext?.RecommendedTitlePatterns ?? [])
        {
            items.Add(ApplyFeedbackPattern(pattern, lead, datePart, isShort));
        }

        return items
            .Select(x => CleanTitle(x, isShort))
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static string BuildDescription(MetadataOptimizationInput input, string[] hashtags, bool isShort)
    {
        var top = input.Context.Events.OrderByDescending(x => x.Score).Take(2).ToArray();
        var line1 = isShort
            ? $"Quick astronomy update for {input.Context.LocationName}."
            : $"Tonight's astronomy guide for {input.Context.LocationName} on {input.Context.Date:MMMM dd, yyyy}.";

        var line2 = top.Length > 0
            ? $"Top highlights: {string.Join(", ", top.Select(x => x.ObjectName))}."
            : "Top highlights picked for easy observing.";

        var tips = isShort
            ? "Observation tip: wait 5 minutes for your eyes to adapt before spotting targets."
            : string.Join(" ", top.Select(x => $"Tip: look {x.Direction} {x.VisibilityWindow} and use {x.ObservationTool}."));

        var experimentHint = input.FeedbackContext?.RecommendedHookPatterns.FirstOrDefault();
        var introLine = string.IsNullOrWhiteSpace(experimentHint)
            ? string.Empty
            : $"Winning hook pattern to reuse: {experimentHint}.\n\n";

        return $"{line1}\n{line2}\n\n{introLine}{tips}\n\n{input.SourceDescription.Trim()}\n\n{string.Join(" ", hashtags)}".Trim();
    }

    private static string[] BuildTags(ContentType type, IReadOnlyCollection<string> sourceTags, IReadOnlyCollection<string>? feedbackKeywords, string[] topObjects, bool isShort)
    {
        var baseTags = new List<string>(sourceTags)
        {
            "astronomy",
            "night sky",
            type.ToString()
        };

        if (feedbackKeywords is not null)
        {
            baseTags.AddRange(feedbackKeywords);
        }

        baseTags.AddRange(topObjects);
        if (isShort)
        {
            baseTags.Add("shorts");
            baseTags.Add("youtube shorts");
        }

        return baseTags
            .Select(NormalizeTag)
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTags)
            .ToArray();
    }

    private static string[] BuildHashtags(ContentTypeMetadataStrategy strategy, string[] topObjects, bool isShort)
    {
        var hashtags = new List<string> { "#astronomy", "#nightsky" };
        hashtags.AddRange(topObjects.Select(ToHashtag));
        hashtags.Add(strategy.ContentTypeHashtag);

        if (isShort)
        {
            hashtags.Add("#shorts");
        }

        return hashtags
            .Select(NormalizeHashtag)
            .Where(static h => h.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxHashtags)
            .ToArray();
    }

    private static string ApplyFeedbackPattern(string pattern, string lead, string datePart, bool isShort)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return string.Empty;

        var normalized = pattern
            .Replace("<Object/Event>", lead, StringComparison.OrdinalIgnoreCase)
            .Replace("<N>", isShort ? "60" : datePart, StringComparison.OrdinalIgnoreCase);

        return normalized.Contains(lead, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{lead}: {normalized}";
    }

    private static string[] BuildThumbnailSuggestions(ContentTypeMetadataStrategy strategy, string[] topObjects, bool isShort)
    {
        var lead = topObjects.FirstOrDefault() ?? "Tonight";
        return
        [
            isShort ? $"{lead} NOW" : $"TONIGHT: {lead}",
            isShort ? "60-SECOND SKY" : strategy.LongThumbnailLabel,
            isShort ? "LOOK UP FAST" : "BEGINNER FRIENDLY"
        ];
    }

    private static OptimizedVideoMetadata Normalize(OptimizedVideoMetadata metadata, ContentType type, bool isShort)
    {
        var strategy = ResolveStrategy(type);
        var fallbackHashtags = BuildHashtags(strategy, Array.Empty<string>(), isShort);
        var fallbackTags = BuildTags(type, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), isShort);

        return new OptimizedVideoMetadata
        {
            PrimaryTitle = CleanTitle(metadata.PrimaryTitle, isShort),
            AlternateTitles = metadata.AlternateTitles
                .Select(x => CleanTitle(x, isShort))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray(),
            OptimizedDescription = metadata.OptimizedDescription.Trim(),
            Tags = metadata.Tags
                .Select(NormalizeTag)
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxTags)
                .DefaultIfEmpty(fallbackTags[0])
                .ToArray(),
            Hashtags = metadata.Hashtags
                .Select(NormalizeHashtag)
                .Where(static h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxHashtags)
                .DefaultIfEmpty(fallbackHashtags[0])
                .ToArray(),
            ThumbnailTextSuggestions = metadata.ThumbnailTextSuggestions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray(),
            HookLine = isShort ? metadata.HookLine?.Trim() : null
        };
    }

    private static ContentTypeMetadataStrategy ResolveStrategy(ContentType type)
        => Strategies.TryGetValue(type, out var strategy)
            ? strategy
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported content type.");

    private static string NormalizeTag(string tag) => Regex.Replace(tag.Trim(), "\\s+", " ").ToLowerInvariant();

    private static string ToHashtag(string value)
        => NormalizeHashtag("#" + Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", ""));

    private static string NormalizeHashtag(string value)
    {
        var cleaned = "#" + Regex.Replace(value.Trim().TrimStart('#').ToLowerInvariant(), "[^a-z0-9]+", "");
        return cleaned == "#" ? string.Empty : cleaned;
    }

    private static string CleanTitle(string title, bool isShort)
    {
        var cleaned = Regex.Replace(title ?? string.Empty, "\\s+", " ").Trim();
        var max = isShort ? 70 : 100;
        if (cleaned.Length > max)
        {
            cleaned = cleaned[..max].TrimEnd();
        }

        return cleaned;
    }

    private sealed record ContentTypeMetadataStrategy(
        Func<string?, string, string, string> LongFormTitle,
        Func<string?, string, string, string> ShortFormTitle,
        string AlternateCategoryLabel,
        string ContentTypeHashtag,
        string LongThumbnailLabel);
}
