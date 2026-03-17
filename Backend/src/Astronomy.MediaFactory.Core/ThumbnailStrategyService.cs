using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailStrategyService : IThumbnailStrategyService
{
    public ThumbnailPlan BuildPlan(ThumbnailGenerationRequest request)
    {
        var objectName = request.Context.Events
            .OrderByDescending(x => x.Score)
            .Select(x => x.ObjectName)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

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
        {
            primary = $"SHORT: {Truncate(primary, 32)}";
        }

        var alternates = request.Metadata.ThumbnailTextSuggestions
            .Concat(BuildAlternates(request.ContentType, objectName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        if (alternates.Length == 0)
            alternates = [primary];

        var layout = request.ContentType switch
        {
            ContentType.DailySkyGuide => ThumbnailLayoutType.TopBanner,
            ContentType.SpaceNews => ThumbnailLayoutType.CenteredTitleOverlay,
            ContentType.AstrophotographyTips => ThumbnailLayoutType.TextLeftVisualRight,
            ContentType.TelescopeTargets => ThumbnailLayoutType.TextLeftVisualRight,
            _ => ThumbnailLayoutType.CenteredTitleOverlay
        };

        if (request.IsShortForm)
            layout = ThumbnailLayoutType.CenteredTitleOverlay;

        return new ThumbnailPlan
        {
            PrimaryThumbnailText = Normalize(primary),
            AlternateThumbnailTexts = alternates,
            SelectedVisualPath = request.AvailableVisuals.FirstOrDefault(File.Exists),
            LayoutType = layout
        };
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
