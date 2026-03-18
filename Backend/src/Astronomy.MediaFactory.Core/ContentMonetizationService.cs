using System.Text;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class ContentMonetizationService : IContentMonetizationService
{
    private static readonly IReadOnlyDictionary<string, ProductCatalogEntry> ProductCatalog = new Dictionary<string, ProductCatalogEntry>(StringComparer.OrdinalIgnoreCase)
    {
        ["beginner-telescope"] = new("beginner-telescope", "Beginner Telescope", "telescope", "A flexible starter option for Moon, planets, and brighter deep-sky targets."),
        ["planetary-eyepiece"] = new("planetary-eyepiece", "Planetary Eyepiece", "telescope-accessory", "Helps viewers get more detail from high-contrast targets like Jupiter and Saturn."),
        ["astronomy-binoculars"] = new("astronomy-binoculars", "Astronomy Binoculars", "binoculars", "Useful for wide-field sky tours, bright clusters, and quick backyard observing."),
        ["star-tracker"] = new("star-tracker", "Star Tracker", "astrophotography-mount", "Makes longer astrophotography exposures easier to capture cleanly."),
        ["mirrorless-camera"] = new("mirrorless-camera", "Mirrorless Camera", "camera", "A simple upgrade path for astrophotography and night-sky imaging."),
        ["sturdy-tripod"] = new("sturdy-tripod", "Sturdy Tripod", "tripod", "Keeps wide-field night-sky shots stable and beginner friendly.")
    };

    private readonly MonetizationOptions _options;
    private readonly ILogger<ContentMonetizationService> _logger;

    public ContentMonetizationService(IOptions<MonetizationOptions> options, ILogger<ContentMonetizationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<MonetizationPlan> BuildPlanAsync(MonetizationInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Context);
        ArgumentNullException.ThrowIfNull(input.Metadata);

        var primarySignal = ResolvePrimarySignal(input);
        var ctaBlocks = BuildCtaBlocks(input.ContentType, input.IsShortForm, primarySignal);
        var sponsorBlock = BuildSponsorBlock(input.IsShortForm);
        var recommendations = BuildRecommendations(input, primarySignal);
        var affiliateLinks = recommendations
            .Where(x => !string.IsNullOrWhiteSpace(x.AffiliateUrl))
            .Select(x => new AffiliateLink
            {
                LinkType = x.Category,
                Label = x.DisplayName,
                Url = x.AffiliateUrl!
            })
            .ToArray();

        var finalDescription = ComposeDescription(input, ctaBlocks, sponsorBlock, recommendations, affiliateLinks);
        var pinnedCommentText = BuildPinnedCommentText(input, ctaBlocks, affiliateLinks);

        _logger.LogInformation(
            "Generated monetization plan for {ContentType}. RecommendedProducts={RecommendedCount}, AffiliateLinks={AffiliateCount}, IsShort={IsShort}",
            input.ContentType,
            recommendations.Count,
            affiliateLinks.Length,
            input.IsShortForm);

        return Task.FromResult(new MonetizationPlan
        {
            FinalDescription = finalDescription,
            PinnedCommentText = pinnedCommentText,
            SponsorBlock = sponsorBlock,
            CtaBlocks = ctaBlocks,
            RecommendedProducts = recommendations,
            AffiliateLinks = affiliateLinks
        });
    }

    private List<ProductRecommendation> BuildRecommendations(MonetizationInput input, ObservingSignal signal)
    {
        var recommendationKeys = new List<string>();

        switch (input.ContentType)
        {
            case ContentType.TelescopeTargets:
                recommendationKeys.Add("beginner-telescope");
                recommendationKeys.Add("planetary-eyepiece");
                break;
            case ContentType.AstrophotographyTips:
                recommendationKeys.Add("mirrorless-camera");
                recommendationKeys.Add("sturdy-tripod");
                recommendationKeys.Add("star-tracker");
                break;
            case ContentType.DailySkyGuide:
                recommendationKeys.Add(signal.IsWideField ? "astronomy-binoculars" : "beginner-telescope");
                recommendationKeys.Add(signal.IsWideField ? "sturdy-tripod" : "planetary-eyepiece");
                break;
            case ContentType.SpaceNews:
                recommendationKeys.Add(signal.IsAstrophotography ? "mirrorless-camera" : "astronomy-binoculars");
                recommendationKeys.Add(signal.IsAstrophotography ? "sturdy-tripod" : "beginner-telescope");
                break;
        }

        if (signal.IsAstrophotography)
        {
            recommendationKeys.Add("mirrorless-camera");
            recommendationKeys.Add("sturdy-tripod");
        }
        else if (signal.IsWideField)
        {
            recommendationKeys.Add("astronomy-binoculars");
        }
        else if (signal.ObservationDifficulty is ObservationDifficulty.Moderate or ObservationDifficulty.Advanced)
        {
            recommendationKeys.Add("beginner-telescope");
        }

        if (input.AnalyticsFeedback?.TopKeywords.Any(keyword => keyword.Contains("astrophotography", StringComparison.OrdinalIgnoreCase)) == true)
        {
            recommendationKeys.Insert(0, "mirrorless-camera");
            recommendationKeys.Insert(1, "sturdy-tripod");
        }

        var maxRecommendations = input.IsShortForm ? 1 : 3;
        return recommendationKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxRecommendations)
            .Select(key => CreateRecommendation(ProductCatalog[key], signal, input.ContentType))
            .ToList();
    }

    private ProductRecommendation CreateRecommendation(ProductCatalogEntry entry, ObservingSignal signal, ContentType contentType)
    {
        var topicSlug = Slugify(string.IsNullOrWhiteSpace(signal.ObjectName) ? contentType.ToString() : signal.ObjectName);
        return new ProductRecommendation
        {
            Key = entry.Key,
            DisplayName = entry.DisplayName,
            Category = entry.Category,
            Reason = entry.DefaultReason,
            AffiliateUrl = BuildAffiliateUrl(entry.Key, contentType, topicSlug)
        };
    }

    private IReadOnlyCollection<string> BuildCtaBlocks(ContentType contentType, bool isShortForm, ObservingSignal signal)
    {
        if (isShortForm)
        {
            return
            [
                signal.IsAstrophotography
                    ? "Want the gear from this short? Links are below."
                    : "Quick gear picks for this target are below."
            ];
        }

        return contentType switch
        {
            ContentType.TelescopeTargets => ["Check out beginner telescopes below.", "Use the recommended eyepiece setup to get a closer look at this target."],
            ContentType.AstrophotographyTips => ["Learn astrophotography with this setup.", "Build a simple night-sky kit with the recommended gear below."],
            ContentType.SpaceNews => ["Explore the gear that makes these discoveries easier to follow from home.", "If you want to observe related objects yourself, start with the picks below."],
            _ => ["Best gear for tonight's sky is listed below.", signal.IsWideField ? "Binocular-friendly gear is included for wide sky views." : "Starter telescope picks are included for deeper views."]
        };
    }

    private string? BuildSponsorBlock(bool isShortForm)
    {
        if (!_options.EnableSponsorSlots || string.IsNullOrWhiteSpace(_options.SponsorText))
        {
            return null;
        }

        return isShortForm
            ? $"Sponsored: {_options.SponsorText!.Trim()}"
            : _options.SponsorText!.Contains("sponsored", StringComparison.OrdinalIgnoreCase)
                ? _options.SponsorText.Trim()
                : $"This video is sponsored by {_options.SponsorText.Trim()}";
    }

    private string ComposeDescription(
        MonetizationInput input,
        IReadOnlyCollection<string> ctaBlocks,
        string? sponsorBlock,
        IReadOnlyCollection<ProductRecommendation> recommendations,
        IReadOnlyCollection<AffiliateLink> affiliateLinks)
    {
        var sections = new List<string>();
        var baseDescription = input.Metadata.OptimizedDescription.Trim();
        if (!string.IsNullOrWhiteSpace(baseDescription))
        {
            sections.Add(baseDescription);
        }

        if (input.IsShortForm && ctaBlocks.Count > 0)
        {
            sections.Add(ctaBlocks.First());
        }

        if (input.Metadata.Hashtags.Length > 0)
        {
            sections.Add(string.Join(' ', input.Metadata.Hashtags));
        }

        if (!string.IsNullOrWhiteSpace(sponsorBlock))
        {
            sections.Add(sponsorBlock);
        }

        if (!input.IsShortForm && ctaBlocks.Count > 0)
        {
            sections.Add(string.Join(Environment.NewLine, ctaBlocks));
        }

        if (affiliateLinks.Count > 0)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Recommended gear:");
            foreach (var link in affiliateLinks)
            {
                builder.Append("- ").Append(link.Label).Append(": ").AppendLine(link.Url);
            }

            builder.AppendLine();
            builder.Append("Disclosure: Some links may be affiliate links.");
            sections.Add(builder.ToString().Trim());
        }
        else if (!input.IsShortForm && recommendations.Count > 0)
        {
            sections.Add($"Recommended products: {string.Join(", ", recommendations.Select(x => x.DisplayName))}.");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private string? BuildPinnedCommentText(MonetizationInput input, IReadOnlyCollection<string> ctaBlocks, IReadOnlyCollection<AffiliateLink> affiliateLinks)
    {
        if (!_options.EnablePinnedCommentText || affiliateLinks.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine(ctaBlocks.FirstOrDefault() ?? "Gear mentioned in this video:");
        foreach (var link in affiliateLinks.Take(input.IsShortForm ? 1 : 2))
        {
            builder.Append("- ").Append(link.Label).Append(": ").AppendLine(link.Url);
        }

        return builder.ToString().Trim();
    }

    private string? BuildAffiliateUrl(string key, ContentType contentType, string topicSlug)
    {
        if (!_options.EnableAffiliateLinks || string.IsNullOrWhiteSpace(_options.AffiliateBaseUrl))
        {
            return null;
        }

        var baseUrl = _options.AffiliateBaseUrl.TrimEnd('/');
        var query = new List<string>
        {
            $"contentType={Uri.EscapeDataString(contentType.ToString())}",
            $"topic={Uri.EscapeDataString(topicSlug)}"
        };

        if (!string.IsNullOrWhiteSpace(_options.DefaultAffiliateTag))
        {
            query.Insert(0, $"tag={Uri.EscapeDataString(_options.DefaultAffiliateTag)}");
        }

        return $"{baseUrl}/{key}?{string.Join("&", query)}";
    }

    private static ObservingSignal ResolvePrimarySignal(MonetizationInput input)
    {
        var selectedObject = input.Context.TopicSelectionPlan?.PrimaryLongForm?.ObjectName;
        var matchedEvent = !string.IsNullOrWhiteSpace(selectedObject)
            ? input.Context.Events.FirstOrDefault(x => x.ObjectName.Equals(selectedObject, StringComparison.OrdinalIgnoreCase))
            : null;
        var primaryEvent = matchedEvent ?? input.Context.Events.OrderByDescending(x => x.Score).FirstOrDefault();
        var tool = primaryEvent?.ObservationTool ?? string.Empty;
        var difficulty = ResolveDifficulty(input.ContentType, tool, primaryEvent?.Details);

        return new ObservingSignal(
            primaryEvent?.ObjectName ?? selectedObject ?? input.Metadata.PrimaryTitle,
            tool,
            difficulty,
            IsWideField(primaryEvent, input.Metadata),
            IsAstrophotography(input.ContentType, tool, primaryEvent?.Details, input.AnalyticsFeedback));
    }

    private static ObservationDifficulty ResolveDifficulty(ContentType contentType, string? observationTool, string? details)
    {
        var text = $"{observationTool} {details} {contentType}";
        if (text.Contains("naked eye", StringComparison.OrdinalIgnoreCase) || text.Contains("binocular", StringComparison.OrdinalIgnoreCase))
        {
            return ObservationDifficulty.Beginner;
        }

        if (text.Contains("small telescope", StringComparison.OrdinalIgnoreCase) || text.Contains("dobsonian", StringComparison.OrdinalIgnoreCase) || text.Contains("refractor", StringComparison.OrdinalIgnoreCase))
        {
            return ObservationDifficulty.Moderate;
        }

        if (text.Contains("astrophotography", StringComparison.OrdinalIgnoreCase) || text.Contains("camera", StringComparison.OrdinalIgnoreCase) || text.Contains("tracking", StringComparison.OrdinalIgnoreCase))
        {
            return ObservationDifficulty.Advanced;
        }

        return contentType == ContentType.AstrophotographyTips ? ObservationDifficulty.Advanced : ObservationDifficulty.Moderate;
    }

    private static bool IsWideField(AstronomyEventModel? primaryEvent, OptimizedVideoMetadata metadata)
    {
        var text = $"{primaryEvent?.ObjectName} {primaryEvent?.ObservationTool} {primaryEvent?.Details} {metadata.PrimaryTitle} {metadata.OptimizedDescription}";
        return text.Contains("binocular", StringComparison.OrdinalIgnoreCase)
            || text.Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || text.Contains("milky way", StringComparison.OrdinalIgnoreCase)
            || text.Contains("wide", StringComparison.OrdinalIgnoreCase)
            || text.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAstrophotography(ContentType contentType, string? observationTool, string? details, FeedbackSignals? analyticsFeedback)
    {
        var text = $"{contentType} {observationTool} {details} {string.Join(' ', analyticsFeedback?.TopKeywords ?? [])}";
        return contentType == ContentType.AstrophotographyTips
            || text.Contains("astrophotography", StringComparison.OrdinalIgnoreCase)
            || text.Contains("camera", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tripod", StringComparison.OrdinalIgnoreCase)
            || text.Contains("long exposure", StringComparison.OrdinalIgnoreCase);
    }

    private static string Slugify(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record ProductCatalogEntry(string Key, string DisplayName, string Category, string DefaultReason);
    private sealed record ObservingSignal(string ObjectName, string ObservationTool, ObservationDifficulty ObservationDifficulty, bool IsWideField, bool IsAstrophotography);
    private enum ObservationDifficulty
    {
        Beginner = 1,
        Moderate = 2,
        Advanced = 3
    }
}
