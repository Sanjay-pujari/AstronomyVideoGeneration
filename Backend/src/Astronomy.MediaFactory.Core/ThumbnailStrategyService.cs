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
            .Concat(BuildAlternates(request.ContentType, objectName, request.Context.Localization.ResolvedLanguage))
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
        var isHindi = LocalizationResolver.IsHindi(request.Context.Localization.ResolvedLanguage);
        var primary = request.ContentType switch
        {
            ContentType.SpecialEventGuide => BuildSpecialEventText(request, objectName),
            ContentType.DailySkyGuide => !string.IsNullOrWhiteSpace(objectName)
                ? isHindi ? $"आज रात: {objectName}" : $"TONIGHT'S SKY: {objectName.ToUpperInvariant()}"
                : isHindi ? "आज रात का आसमान" : "TONIGHT'S SKY",
            ContentType.TelescopeTargets => !string.IsNullOrWhiteSpace(objectName)
                ? isHindi ? $"बेहतरीन लक्ष्य: {objectName}" : $"BEST TARGETS: {objectName.ToUpperInvariant()}"
                : isHindi ? "आज रात के बेहतरीन लक्ष्य" : "BEST TARGETS TONIGHT",
            ContentType.SpaceNews => BuildSpaceNewsText(request.Context),
            ContentType.AstrophotographyTips => !string.IsNullOrWhiteSpace(objectName)
                ? isHindi ? $"फोटो टिप: {objectName}" : $"PHOTO TIP: {objectName.ToUpperInvariant()}"
                : isHindi ? "आज की एस्ट्रोफोटो टिप" : "ASTROPHOTO TIP TONIGHT",
            _ => isHindi ? "खगोल अपडेट" : "ASTRONOMY UPDATE"
        };

        if (request.IsShortForm)
            primary = LocalizationResolver.IsHindi(request.Context.Localization.ResolvedLanguage) ? $"शॉर्ट: {Truncate(primary, 32)}" : $"SHORT: {Truncate(primary, 32)}";

        return primary;
    }

    private static string BuildSpecialEventText(ThumbnailGenerationRequest request, string? objectName)
    {
        var title = request.Context.SpecialEvent?.EventTitle;
        if (!string.IsNullOrWhiteSpace(title))
            return Truncate(title.ToUpperInvariant(), 42);

        return !string.IsNullOrWhiteSpace(objectName)
            ? $"WATCH: {objectName.ToUpperInvariant()}"
            : "RARE SKY EVENT";
    }


    private static ThumbnailLayoutType[] BuildLayoutCandidates(ThumbnailGenerationRequest request)
    {
        if (request.IsShortForm)
            return [ThumbnailLayoutType.CenteredTitleOverlay, ThumbnailLayoutType.TopBanner, ThumbnailLayoutType.TextLeftVisualRight];

        var preferredByContentType = request.ContentType switch
        {
            ContentType.SpecialEventGuide => ThumbnailLayoutType.CenteredTitleOverlay,
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

    private static IEnumerable<string> BuildAlternates(ContentType contentType, string? objectName, string language)
    {
        var isHindi = LocalizationResolver.IsHindi(language);
        yield return contentType switch
        {
            ContentType.SpecialEventGuide => isHindi ? "यह खगोलीय घटना देखें" : "WATCH THIS SKY EVENT",
            ContentType.DailySkyGuide => isHindi ? "आज रात की प्रमुख घटनाएं" : "TONIGHT'S TOP SKY EVENTS",
            ContentType.TelescopeTargets => isHindi ? "टेलिस्कोप के आसान लक्ष्य" : "EASY TARGETS FOR YOUR SCOPE",
            ContentType.SpaceNews => isHindi ? "अंतरिक्ष में नया क्या है" : "WHAT'S NEW IN SPACE",
            ContentType.AstrophotographyTips => isHindi ? "बेहतर रात का आसमान कैप्चर करें" : "CAPTURE BETTER NIGHT SKIES",
            _ => isHindi ? "खगोल हाइलाइट्स" : "ASTRONOMY HIGHLIGHTS"
        };

        if (!string.IsNullOrWhiteSpace(objectName))
            yield return isHindi ? $"आज रात {objectName}" : $"{objectName.ToUpperInvariant()} TONIGHT";
    }

    private static string Normalize(string text) => Truncate(text.Trim(), 48);

    private static string Truncate(string input, int max)
        => input.Length <= max ? input : input[..max].TrimEnd() + "…";
}
