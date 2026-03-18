using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailStrategyService : IThumbnailStrategyService
{
    private static readonly ThumbnailLayoutType[] ResilientFallbackOrder =
    [
        ThumbnailLayoutType.CenteredTitleOverlay,
        ThumbnailLayoutType.TopBanner,
        ThumbnailLayoutType.TextLeftVisualRight
    ];

    public ThumbnailPlan BuildPlan(ThumbnailGenerationRequest request)
    {
        var objectName = request.Context.Events
            .OrderByDescending(x => x.Score)
            .Select(x => x.ObjectName)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        var primary = BuildPrimaryText(request, objectName);

        var alternates = request.Metadata.ThumbnailTextSuggestions
            .Concat(request.Context.PromptFeedbackContext?.ThumbnailStrategyHints ?? [])
            .Concat(BuildAlternates(request.ContentType, objectName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        if (alternates.Length == 0)
            alternates = [primary];

        var layoutCandidates = BuildLayoutCandidates(request);
        var variantOptions = BuildVariants(primary, alternates, layoutCandidates);

        return new ThumbnailPlan
        {
            PrimaryThumbnailText = Normalize(primary),
            AlternateThumbnailTexts = alternates,
            SelectedVisualPath = request.AvailableVisuals.FirstOrDefault(File.Exists),
            LayoutType = layoutCandidates[0],
            LayoutCandidates = layoutCandidates,
            Variants = variantOptions
        };
    }

    private static IReadOnlyCollection<ThumbnailVariantOption> BuildVariants(string primary, IReadOnlyCollection<string> alternates, IReadOnlyCollection<ThumbnailLayoutType> layouts)
    {
        var texts = new[] { primary }
            .Concat(alternates)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        return layouts
            .SelectMany(layout => texts.Select(text => new ThumbnailVariantOption
            {
                LayoutType = layout,
                Text = text
            }))
            .Take(4)
            .ToArray();
    }

    private static string BuildPrimaryText(ThumbnailGenerationRequest request, string? objectName)
    {
        var primary = request.ContentType switch
        {
            ContentType.DailySkyGuide => !string.IsNullOrWhiteSpace(objectName)
                ? $"TONIGHT'S SKY: {objectName.ToUpperInvariant()}"
                : "TONIGHT'S SKY",
            ContentType.TelescopeTargets => !string.IsNullOrWhiteSpace(objectName)
                ? $"BEST TARGETS: {objectName.ToUpperInvariant()}"
                : "BEST TARGETS TONIGHT",
            ContentType.SpaceNews => BuildSpaceNewsText(request.Context),
            ContentType.AstrophotographyTips => !string.IsNullOrWhiteSpace(objectName)
                ? $"PHOTO TIP: {objectName.ToUpperInvariant()}"
                : "ASTROPHOTO TIP TONIGHT",
            _ => "ASTRONOMY UPDATE"
        };

        if (request.IsShortForm)
            primary = $"SHORT: {Truncate(primary, 32)}";

        return primary;
    }

    private static ThumbnailLayoutType[] BuildLayoutCandidates(ThumbnailGenerationRequest request)
    {
        if (request.IsShortForm)
            return [ThumbnailLayoutType.CenteredTitleOverlay, ThumbnailLayoutType.TopBanner, ThumbnailLayoutType.TextLeftVisualRight];

        var preferredByContentType = request.ContentType switch
        {
            ContentType.DailySkyGuide => ThumbnailLayoutType.TopBanner,
            ContentType.SpaceNews => ThumbnailLayoutType.CenteredTitleOverlay,
            ContentType.AstrophotographyTips => ThumbnailLayoutType.TextLeftVisualRight,
            ContentType.TelescopeTargets => ThumbnailLayoutType.TextLeftVisualRight,
            _ => ThumbnailLayoutType.CenteredTitleOverlay
        };

        var scoredLayouts = ResilientFallbackOrder
            .ToDictionary(layout => layout, _ => 0d);

        scoredLayouts[preferredByContentType] += 1.0;

        var topKeywords = request.FeedbackSignals?.TopKeywords ?? [];
        foreach (var keyword in topKeywords)
        {
            if (keyword.Contains("tonight", StringComparison.OrdinalIgnoreCase)
                || keyword.Contains("guide", StringComparison.OrdinalIgnoreCase))
            {
                scoredLayouts[ThumbnailLayoutType.TopBanner] += 0.25;
            }

            if (keyword.Contains("discover", StringComparison.OrdinalIgnoreCase)
                || keyword.Contains("news", StringComparison.OrdinalIgnoreCase)
                || keyword.Contains("update", StringComparison.OrdinalIgnoreCase))
            {
                scoredLayouts[ThumbnailLayoutType.CenteredTitleOverlay] += 0.25;
            }

            if (keyword.Contains("photo", StringComparison.OrdinalIgnoreCase)
                || keyword.Contains("target", StringComparison.OrdinalIgnoreCase))
            {
                scoredLayouts[ThumbnailLayoutType.TextLeftVisualRight] += 0.25;
            }
        }

        foreach (var hint in request.Context.PromptFeedbackContext?.ThumbnailStrategyHints ?? [])
        {
            if (hint.Contains(nameof(ThumbnailLayoutType.TopBanner), StringComparison.OrdinalIgnoreCase)
                || hint.Contains("banner", StringComparison.OrdinalIgnoreCase))
            {
                scoredLayouts[ThumbnailLayoutType.TopBanner] += 0.4;
            }

            if (hint.Contains(nameof(ThumbnailLayoutType.CenteredTitleOverlay), StringComparison.OrdinalIgnoreCase)
                || hint.Contains("overlay", StringComparison.OrdinalIgnoreCase))
            {
                scoredLayouts[ThumbnailLayoutType.CenteredTitleOverlay] += 0.4;
            }

            if (hint.Contains(nameof(ThumbnailLayoutType.TextLeftVisualRight), StringComparison.OrdinalIgnoreCase)
                || hint.Contains("left", StringComparison.OrdinalIgnoreCase))
            {
                scoredLayouts[ThumbnailLayoutType.TextLeftVisualRight] += 0.4;
            }
        }

        return scoredLayouts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => Array.IndexOf(ResilientFallbackOrder, x.Key))
            .Select(x => x.Key)
            .ToArray();
    }

    private static string BuildSpaceNewsText(AstronomyContext context)
    {
        var headline = context.NewsItems.FirstOrDefault()?.Headline;
        if (string.IsNullOrWhiteSpace(headline))
            return "SPACE NEWS UPDATE";

        var emphasis = headline.Contains("discover", StringComparison.OrdinalIgnoreCase)
            ? "NEW DISCOVERY"
            : "SPACE NEWS";

        return $"{emphasis}: {Truncate(headline.ToUpperInvariant(), 34)}";
    }

    private static IEnumerable<string> BuildAlternates(ContentType contentType, string? objectName)
    {
        yield return contentType switch
        {
            ContentType.DailySkyGuide => "TONIGHT'S TOP SKY EVENTS",
            ContentType.TelescopeTargets => "EASY TARGETS FOR YOUR SCOPE",
            ContentType.SpaceNews => "WHAT'S NEW IN SPACE",
            ContentType.AstrophotographyTips => "CAPTURE BETTER NIGHT SKIES",
            _ => "ASTRONOMY HIGHLIGHTS"
        };

        if (!string.IsNullOrWhiteSpace(objectName))
            yield return $"{objectName.ToUpperInvariant()} TONIGHT";
    }

    private static string Normalize(string text) => Truncate(text.Trim(), 48);

    private static string Truncate(string input, int max)
        => input.Length <= max ? input : input[..max].TrimEnd() + "…";
}
