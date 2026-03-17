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

    private readonly IMetadataOptimizationModelClient? _modelClient;
    private readonly ILogger<MetadataOptimizationService> _logger;

    public MetadataOptimizationService(ILogger<MetadataOptimizationService> logger, IMetadataOptimizationModelClient? modelClient = null)
    {
        _logger = logger;
        _modelClient = modelClient;
    }

    public async Task<OptimizedVideoMetadata> OptimizeForVideoAsync(MetadataOptimizationInput input, CancellationToken cancellationToken)
    {
        if (_modelClient is not null)
        {
            try
            {
                var aiResult = await _modelClient.TryOptimizeAsync(input, isShort: false, cancellationToken);
                if (aiResult is not null)
                {
                    return Normalize(aiResult, input.ContentType, isShort: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI metadata optimization failed for long-form video. Falling back to deterministic strategy.");
            }
        }

        return BuildDeterministic(input, isShort: false);
    }

    public async Task<OptimizedVideoMetadata> OptimizeForShortAsync(MetadataOptimizationInput input, CancellationToken cancellationToken)
    {
        if (_modelClient is not null)
        {
            try
            {
                var aiResult = await _modelClient.TryOptimizeAsync(input, isShort: true, cancellationToken);
                if (aiResult is not null)
                {
                    return Normalize(aiResult, input.ContentType, isShort: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI metadata optimization failed for short. Falling back to deterministic strategy.");
            }
        }

        return BuildDeterministic(input, isShort: true);
    }

    private OptimizedVideoMetadata BuildDeterministic(MetadataOptimizationInput input, bool isShort)
    {
        var topObjects = input.Context.Events.OrderByDescending(e => e.Score).Take(3).Select(e => e.ObjectName).Where(static n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var datePart = input.Context.Date.ToString("MMM dd");

        var primaryTitle = BuildTitle(input.ContentType, input.SourceTitle, topObjects, datePart, isShort);
        var alternates = BuildAlternates(input.ContentType, topObjects, datePart, isShort);
        var hashtags = BuildHashtags(input.ContentType, topObjects, isShort);
        var tags = BuildTags(input.ContentType, input.SourceTags, topObjects, isShort);
        var description = BuildDescription(input, hashtags, isShort);
        var hook = isShort ? (string.IsNullOrWhiteSpace(input.SourceHookLine) ? $"Tonight's quick sky highlight: {topObjects.FirstOrDefault() ?? "look up after sunset"}." : input.SourceHookLine!.Trim()) : null;

        return new OptimizedVideoMetadata
        {
            PrimaryTitle = primaryTitle,
            AlternateTitles = alternates,
            OptimizedDescription = description,
            Tags = tags,
            Hashtags = hashtags,
            ThumbnailTextSuggestions = BuildThumbnailSuggestions(input.ContentType, topObjects, isShort),
            HookLine = hook
        };
    }

    private static string BuildTitle(ContentType type, string sourceTitle, string[] topObjects, string datePart, bool isShort)
    {
        var objectPart = topObjects.FirstOrDefault();
        return type switch
        {
            ContentType.DailySkyGuide => objectPart is null ? $"Tonight's Sky Guide ({datePart})" : $"Tonight's Sky: {objectPart} & More ({datePart})",
            ContentType.TelescopeTargets => objectPart is null ? "Best Telescope Targets for Beginners Tonight" : $"Beginner Telescope Target: {objectPart} Tonight",
            ContentType.SpaceNews => objectPart is null ? CleanTitle(sourceTitle, isShort) : $"Space Update: {objectPart} Explained",
            ContentType.AstrophotographyTips => objectPart is null ? "Astrophotography Tips for Beginners Tonight" : $"Photograph {objectPart}: Beginner Camera Settings",
            _ => CleanTitle(sourceTitle, isShort)
        };
    }

    private static string[] BuildAlternates(ContentType type, string[] topObjects, string datePart, bool isShort)
    {
        var lead = topObjects.FirstOrDefault() ?? "Night Sky";
        var items = new List<string>
        {
            isShort ? $"{lead} in 60 Seconds" : $"{lead} Sky Guide for {datePart}",
            isShort ? $"Quick Space Fact: {lead}" : $"What to Watch Tonight: {lead}",
            isShort ? "Look Up Tonight #shorts" : $"Easy {type} Astronomy Guide"
        };
        return items.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
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

        return $"{line1}\n{line2}\n\n{tips}\n\n{input.SourceDescription.Trim()}\n\n{string.Join(" ", hashtags)}".Trim();
    }

    private static string[] BuildTags(ContentType type, IReadOnlyCollection<string> sourceTags, string[] topObjects, bool isShort)
    {
        var baseTags = new List<string>(sourceTags)
        {
            "astronomy",
            "night sky",
            type.ToString()
        };
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

    private static string[] BuildHashtags(ContentType type, string[] topObjects, bool isShort)
    {
        var hashtags = new List<string> { "#astronomy", "#nightsky" };
        hashtags.AddRange(topObjects.Select(ToHashtag));
        hashtags.Add(type switch
        {
            ContentType.DailySkyGuide => "#tonightsky",
            ContentType.TelescopeTargets => "#telescope",
            ContentType.SpaceNews => "#spacenews",
            ContentType.AstrophotographyTips => "#astrophotography",
            _ => "#space"
        });

        if (isShort)
        {
            hashtags.Add("#shorts");
        }

        return hashtags.Where(static h => h.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxHashtags).ToArray();
    }

    private static string[] BuildThumbnailSuggestions(ContentType type, string[] topObjects, bool isShort)
    {
        var lead = topObjects.FirstOrDefault() ?? "Tonight";
        return
        [
            isShort ? $"{lead} NOW" : $"TONIGHT: {lead}",
            type == ContentType.SpaceNews ? "NEW SPACE UPDATE" : "EASY SKY GUIDE",
            isShort ? "60-SECOND SKY" : "BEGINNER FRIENDLY"
        ];
    }

    private static OptimizedVideoMetadata Normalize(OptimizedVideoMetadata metadata, ContentType type, bool isShort)
    {
        return new OptimizedVideoMetadata
        {
            PrimaryTitle = CleanTitle(metadata.PrimaryTitle, isShort),
            AlternateTitles = metadata.AlternateTitles.Select(x => CleanTitle(x, isShort)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray(),
            OptimizedDescription = metadata.OptimizedDescription.Trim(),
            Tags = metadata.Tags.Select(NormalizeTag).Where(static t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTags).ToArray(),
            Hashtags = metadata.Hashtags.Select(NormalizeHashtag).Where(static h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxHashtags).ToArray(),
            ThumbnailTextSuggestions = metadata.ThumbnailTextSuggestions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray(),
            HookLine = isShort ? metadata.HookLine?.Trim() : null
        };
    }

    private static string NormalizeTag(string tag) => Regex.Replace(tag.Trim(), "\\s+", " ").ToLowerInvariant();
    private static string ToHashtag(string value) => NormalizeHashtag("#" + Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", ""));
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
}
