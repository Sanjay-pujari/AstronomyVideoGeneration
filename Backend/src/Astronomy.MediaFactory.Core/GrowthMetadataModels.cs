using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class GrowthMetadata
{
    public bool Enabled { get; init; }
    public string CtaVariant { get; init; } = "standard";
    public string PlatformCta { get; init; } = "";
    public string DefaultCallToAction { get; init; } = "";
    public string? WebsiteUrl { get; init; }
    public string? NewsletterUrl { get; init; }
    public string? AppDownloadUrl { get; init; }
    public bool AffiliateBlockEnabled { get; init; }
    public string? AffiliateDisclosure { get; init; }
    public string Language { get; init; } = "en";
    public string? Region { get; init; }
    public string Platform { get; init; } = "YouTube";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class GrowthMetadataInput
{
    public required string Platform { get; init; }
    public required string Language { get; init; }
    public string? Region { get; init; }
    public required bool IsShortForm { get; init; }
    public ContentType ContentType { get; init; } = ContentType.DailySkyGuide;
}
