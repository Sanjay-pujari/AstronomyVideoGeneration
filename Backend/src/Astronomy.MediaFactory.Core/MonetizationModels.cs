using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class MonetizationInput
{
    public required ContentType ContentType { get; init; }
    public required AstronomyContext Context { get; init; }
    public required OptimizedVideoMetadata Metadata { get; init; }
    public FeedbackSignals? AnalyticsFeedback { get; init; }
    public bool IsShortForm { get; init; }
}

public sealed class MonetizationPlan
{
    public string FinalDescription { get; init; } = "";
    public string? PinnedCommentText { get; init; }
    public string? SponsorBlock { get; init; }
    public IReadOnlyCollection<string> CtaBlocks { get; init; } = [];
    public IReadOnlyCollection<ProductRecommendation> RecommendedProducts { get; init; } = [];
    public IReadOnlyCollection<AffiliateLink> AffiliateLinks { get; init; } = [];
}

public sealed class ProductRecommendation
{
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Reason { get; init; } = "";
    public string? AffiliateUrl { get; init; }
}

public sealed class AffiliateLink
{
    public string LinkType { get; init; } = "";
    public string Label { get; init; } = "";
    public string Url { get; init; } = "";
}
