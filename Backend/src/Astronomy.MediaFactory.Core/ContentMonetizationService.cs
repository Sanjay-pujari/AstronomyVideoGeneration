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

    private static readonly IReadOnlyDictionary<ContentType, RecommendationRule[]> RecommendationRules =
        new Dictionary<ContentType, RecommendationRule[]>
        {
            [ContentType.TelescopeTargets] =
            [
                new("beginner-telescope", 10, (_, _) => true),
                new("planetary-eyepiece", 20, (_, _) => true),
                new("astronomy-binoculars", 30, (signal, _) => signal.IsWideField)
            ],
            [ContentType.AstrophotographyTips] =
            [
                new("mirrorless-camera", 10, (_, _) => true),
                new("sturdy-tripod", 20, (_, _) => true),
                new("star-tracker", 30, (_, _) => true)
            ],
            [ContentType.DailySkyGuide] =
            [
                new("astronomy-binoculars", 10, (signal, _) => signal.IsWideField),
                new("beginner-telescope", 10, (signal, _) => !signal.IsWideField),
                new("sturdy-tripod", 20, (signal, _) => signal.IsWideField),
                new("planetary-eyepiece", 20, (signal, _) => !signal.IsWideField),
                new("mirrorless-camera", 30, (signal, _) => signal.IsAstrophotography)
            ],
            [ContentType.SpaceNews] =
            [
                new("mirrorless-camera", 10, (signal, _) => signal.IsAstrophotography),
                new("astronomy-binoculars", 10, (signal, _) => !signal.IsAstrophotography),
                new("sturdy-tripod", 20, (signal, _) => signal.IsAstrophotography),
                new("beginner-telescope", 20, (signal, _) => !signal.IsAstrophotography)
            ]
        };

    private static readonly RecommendationRule[] SignalBoosters =
    [
        new("mirrorless-camera", 110, (signal, input) => signal.IsAstrophotography || HasAnalyticsKeyword(input, "astrophotography")),
        new("sturdy-tripod", 120, (signal, input) => signal.IsAstrophotography || HasAnalyticsKeyword(input, "astrophotography")),
        new("star-tracker", 130, (signal, _) => signal.IsAstrophotography && signal.ObservationDifficulty == ObservationDifficulty.Advanced),
        new("astronomy-binoculars", 140, (signal, _) => signal.IsWideField),
        new("beginner-telescope", 150, (signal, _) => signal.ObservationDifficulty is ObservationDifficulty.Moderate or ObservationDifficulty.Advanced)
    ];

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
        var ctaSections = BuildCtaSections(input.ContentType, input.IsShortForm, primarySignal);
        var sponsorBlock = BuildSponsorBlock(input.IsShortForm);
        var recommendations = BuildRecommendations(input, primarySignal);
        var affiliateLinks = BuildAffiliateLinks(recommendations);
        var finalDescription = ComposeDescription(input, ctaSections, sponsorBlock, recommendations, affiliateLinks);
        var pinnedCommentText = BuildPinnedCommentText(input, ctaSections, affiliateLinks);

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
            CtaSections = ctaSections,
            CtaBlocks = ctaSections.Select(x => x.Text).ToArray(),
            RecommendedProducts = recommendations,
            AffiliateLinks = affiliateLinks
        });
    }

    private List<ProductRecommendation> BuildRecommendations(MonetizationInput input, ObservingSignal signal)
    {
        var catalog = RecommendationRules.TryGetValue(input.ContentType, out var mappedRules)
            ? mappedRules.Concat(SignalBoosters)
            : SignalBoosters;

        var maxRecommendations = input.IsShortForm ? 1 : 3;
        return catalog
            .Where(rule => rule.Applies(signal, input))
            .OrderBy(rule => rule.Priority)
            .Select(rule => CreateRecommendation(ProductCatalog[rule.Key], signal, input.ContentType))
            .DistinctBy(recommendation => recommendation.Key, StringComparer.OrdinalIgnoreCase)
            .Take(maxRecommendations)
            .ToList();
    }

    private ProductRecommendation CreateRecommendation(ProductCatalogEntry entry, ObservingSignal signal, ContentType contentType)
    {
        var topicSlug = Slugify(string.IsNullOrWhiteSpace(signal.ObjectName) ? contentType.ToString() : signal.ObjectName);
        var affiliateContext = BuildAffiliateContext(entry, contentType, topicSlug);

        return new ProductRecommendation
        {
            Key = entry.Key,
            DisplayName = entry.DisplayName,
            Category = entry.Category,
            Reason = entry.DefaultReason,
            AffiliateUrl = affiliateContext?.Url,
            AffiliateProductKey = affiliateContext?.ProductKey ?? entry.Key,
            AffiliateMerchant = affiliateContext?.Merchant,
            AffiliateTrackingTag = affiliateContext?.TrackingTag
        };
    }

    private static AffiliateLink[] BuildAffiliateLinks(IReadOnlyCollection<ProductRecommendation> recommendations)
        => recommendations
            .Where(x => !string.IsNullOrWhiteSpace(x.AffiliateUrl))
            .Select(BuildAffiliateLink)
            .ToArray();

    private static AffiliateLink BuildAffiliateLink(ProductRecommendation recommendation)
        => new()
        {
            LinkType = recommendation.Category,
            Label = recommendation.DisplayName,
            Url = recommendation.AffiliateUrl!,
            ProductKey = recommendation.AffiliateProductKey,
            Merchant = recommendation.AffiliateMerchant,
            TrackingTag = recommendation.AffiliateTrackingTag
        };

    private IReadOnlyCollection<CtaSection> BuildCtaSections(ContentType contentType, bool isShortForm, ObservingSignal signal)
    {
        if (isShortForm)
        {
            return
            [
                new(
                    CtaPlacement.DescriptionLead,
                    signal.IsAstrophotography
                        ? "Want the gear from this short? Links are below."
                        : "Quick gear picks for this target are below."),
                new(CtaPlacement.PinnedCommentLead, "Gear mentioned in this short:")
            ];
        }

        return contentType switch
        {
            ContentType.TelescopeTargets =>
            [
                new(CtaPlacement.DescriptionBody, "Check out beginner telescopes below."),
                new(CtaPlacement.DescriptionBody, "Use the recommended eyepiece setup to get a closer look at this target."),
                new(CtaPlacement.PinnedCommentLead, "Gear mentioned in this telescope guide:")
            ],
            ContentType.AstrophotographyTips =>
            [
                new(CtaPlacement.DescriptionBody, "Learn astrophotography with this setup."),
                new(CtaPlacement.DescriptionBody, "Build a simple night-sky kit with the recommended gear below."),
                new(CtaPlacement.PinnedCommentLead, "Astrophotography starter gear from this video:")
            ],
            ContentType.SpaceNews =>
            [
                new(CtaPlacement.DescriptionBody, "Explore the gear that makes these discoveries easier to follow from home."),
                new(CtaPlacement.DescriptionBody, "If you want to observe related objects yourself, start with the picks below."),
                new(CtaPlacement.PinnedCommentLead, "Observation gear related to this story:")
            ],
            _ =>
            [
                new(CtaPlacement.DescriptionBody, "Best gear for tonight's sky is listed below."),
                new(CtaPlacement.DescriptionBody, signal.IsWideField ? "Binocular-friendly gear is included for wide sky views." : "Starter telescope picks are included for deeper views."),
                new(CtaPlacement.PinnedCommentLead, "Tonight's observing gear picks:")
            ]
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
        IReadOnlyCollection<CtaSection> ctaSections,
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

        var leadCta = ctaSections.Where(x => x.Placement == CtaPlacement.DescriptionLead).Select(x => x.Text).ToArray();
        if (leadCta.Length > 0)
        {
            sections.Add(string.Join(Environment.NewLine, leadCta));
        }

        if (input.Metadata.Hashtags.Length > 0)
        {
            sections.Add(string.Join(' ', input.Metadata.Hashtags));
        }

        if (!string.IsNullOrWhiteSpace(sponsorBlock))
        {
            sections.Add(sponsorBlock);
        }

        var bodyCta = ctaSections.Where(x => x.Placement == CtaPlacement.DescriptionBody).Select(x => x.Text).ToArray();
        if (bodyCta.Length > 0)
        {
            sections.Add(string.Join(Environment.NewLine, bodyCta));
        }

        if (affiliateLinks.Count > 0)
        {
            sections.Add(FormatAffiliateSection(affiliateLinks));
        }
        else if (!input.IsShortForm && recommendations.Count > 0)
        {
            sections.Add($"Recommended products: {string.Join(", ", recommendations.Select(x => x.DisplayName))}.");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string FormatAffiliateSection(IReadOnlyCollection<AffiliateLink> affiliateLinks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Recommended gear:");
        foreach (var link in affiliateLinks)
        {
            builder.Append("- ").Append(link.Label).Append(": ").AppendLine(link.Url);
        }

        builder.AppendLine();
        builder.Append("Disclosure: Some links may be affiliate links.");
        return builder.ToString().Trim();
    }

    private string? BuildPinnedCommentText(MonetizationInput input, IReadOnlyCollection<CtaSection> ctaSections, IReadOnlyCollection<AffiliateLink> affiliateLinks)
    {
        if (!_options.EnablePinnedCommentText || affiliateLinks.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        var pinnedLead = ctaSections.FirstOrDefault(x => x.Placement == CtaPlacement.PinnedCommentLead)?.Text;
        builder.AppendLine(pinnedLead ?? "Gear mentioned in this video:");
        foreach (var link in affiliateLinks.Take(input.IsShortForm ? 1 : 2))
        {
            builder.Append("- ").Append(link.Label).Append(": ").AppendLine(link.Url);
        }

        return builder.ToString().Trim();
    }

    private AffiliateContext? BuildAffiliateContext(ProductCatalogEntry entry, ContentType contentType, string topicSlug)
    {
        if (!_options.EnableAffiliateLinks || string.IsNullOrWhiteSpace(_options.AffiliateBaseUrl))
        {
            return null;
        }

        var query = new List<string>
        {
            $"contentType={Uri.EscapeDataString(contentType.ToString())}",
            $"topic={Uri.EscapeDataString(topicSlug)}"
        };

        if (!string.IsNullOrWhiteSpace(_options.DefaultAffiliateTag))
        {
            query.Insert(0, $"tag={Uri.EscapeDataString(_options.DefaultAffiliateTag)}");
        }

        return new AffiliateContext(
            Url: $"{_options.AffiliateBaseUrl.TrimEnd('/')}/{entry.Key}?{string.Join("&", query)}",
            ProductKey: entry.Key,
            Merchant: ResolveAffiliateMerchant(_options.AffiliateBaseUrl),
            TrackingTag: string.IsNullOrWhiteSpace(_options.DefaultAffiliateTag) ? null : _options.DefaultAffiliateTag);
    }

    private static string ResolveAffiliateMerchant(string affiliateBaseUrl)
    {
        if (!Uri.TryCreate(affiliateBaseUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return "custom-network";
        }

        var hostParts = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return hostParts.Length >= 2 ? hostParts[^2] : uri.Host;
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

    private static bool HasAnalyticsKeyword(MonetizationInput input, string keyword)
        => input.AnalyticsFeedback?.TopKeywords.Any(value => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)) == true;

    private static string Slugify(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record ProductCatalogEntry(string Key, string DisplayName, string Category, string DefaultReason);
    private sealed record RecommendationRule(string Key, int Priority, Func<ObservingSignal, MonetizationInput, bool> Applies);
    private sealed record ObservingSignal(string ObjectName, string ObservationTool, ObservationDifficulty ObservationDifficulty, bool IsWideField, bool IsAstrophotography);
    private sealed record AffiliateContext(string Url, string ProductKey, string Merchant, string? TrackingTag);
    private enum ObservationDifficulty
    {
        Beginner = 1,
        Moderate = 2,
        Advanced = 3
    }
}
