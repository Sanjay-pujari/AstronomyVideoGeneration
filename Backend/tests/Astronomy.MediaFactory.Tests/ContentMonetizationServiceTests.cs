using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ContentMonetizationServiceTests
{
    [Fact]
    public async Task BuildPlanAsync_GeneratesAffiliateLinksAndPinnedComment()
    {
        var service = BuildService(new MonetizationOptions
        {
            AffiliateBaseUrl = "https://partners.example.com/products",
            DefaultAffiliateTag = "astro-42",
            EnableAffiliateLinks = true,
            EnablePinnedCommentText = true
        });

        var result = await service.BuildPlanAsync(new MonetizationInput
        {
            ContentType = ContentType.TelescopeTargets,
            Context = BuildContext("small telescope"),
            Metadata = BuildMetadata()
        }, CancellationToken.None);

        Assert.NotEmpty(result.AffiliateLinks);
        Assert.Contains("tag=astro-42", result.AffiliateLinks.First().Url);
        Assert.Contains("https://partners.example.com/products", result.AffiliateLinks.First().Url);
        Assert.Equal("beginner-telescope", result.AffiliateLinks.First().ProductKey);
        Assert.Equal("example", result.AffiliateLinks.First().Merchant);
        Assert.Equal("astro-42", result.AffiliateLinks.First().TrackingTag);
        Assert.Contains("Beginner Telescope", result.PinnedCommentText);
    }

    [Fact]
    public async Task BuildPlanAsync_InsertsCtaAndFormatsDescription()
    {
        var service = BuildService(new MonetizationOptions
        {
            AffiliateBaseUrl = "https://partners.example.com/products",
            EnableAffiliateLinks = true
        });

        var result = await service.BuildPlanAsync(new MonetizationInput
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildContext("binoculars", details: "wide field conjunction"),
            Metadata = BuildMetadata()
        }, CancellationToken.None);

        Assert.Contains("#astronomy", result.FinalDescription);
        Assert.Contains("Best gear for tonight's sky is listed below.", result.FinalDescription);
        Assert.Contains("Recommended gear:", result.FinalDescription);
        Assert.Contains("Disclosure: Some links may be affiliate links.", result.FinalDescription);
        Assert.Contains(result.CtaSections, x => x.Placement == CtaPlacement.DescriptionBody && x.Text == "Best gear for tonight's sky is listed below.");
    }

    [Fact]
    public async Task BuildPlanAsync_UsesContentSpecificRecommendations_ForAstrophotography()
    {
        var service = BuildService(new MonetizationOptions
        {
            AffiliateBaseUrl = "https://partners.example.com/products",
            EnableAffiliateLinks = true
        });

        var result = await service.BuildPlanAsync(new MonetizationInput
        {
            ContentType = ContentType.AstrophotographyTips,
            Context = BuildContext("camera", details: "astrophotography workflow"),
            Metadata = BuildMetadata(),
            AnalyticsFeedback = new FeedbackSignals { TopKeywords = ["astrophotography"] }
        }, CancellationToken.None);

        Assert.Contains(result.RecommendedProducts, x => x.DisplayName == "Mirrorless Camera");
        Assert.Contains(result.RecommendedProducts, x => x.DisplayName == "Sturdy Tripod");
        Assert.Equal(["mirrorless-camera", "sturdy-tripod", "star-tracker"], result.RecommendedProducts.Select(x => x.Key).ToArray());
    }

    [Fact]
    public async Task BuildPlanAsync_KeepsShortsMonetizationMinimal()
    {
        var service = BuildService(new MonetizationOptions
        {
            AffiliateBaseUrl = "https://partners.example.com/products",
            EnableAffiliateLinks = true,
            EnableSponsorSlots = true,
            SponsorText = "SkyMaps Pro"
        });

        var result = await service.BuildPlanAsync(new MonetizationInput
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildContext("binoculars"),
            Metadata = BuildMetadata(),
            IsShortForm = true
        }, CancellationToken.None);

        Assert.Single(result.RecommendedProducts);
        Assert.Single(result.AffiliateLinks);
        Assert.Contains("Quick gear picks for this target are below.", result.FinalDescription);
        Assert.Contains("Sponsored: SkyMaps Pro", result.FinalDescription);
        Assert.Contains(result.CtaSections, x => x.Placement == CtaPlacement.PinnedCommentLead);
    }

    private static ContentMonetizationService BuildService(MonetizationOptions options)
        => new(Options.Create(options), NullLogger<ContentMonetizationService>.Instance);

    private static AstronomyContext BuildContext(string observationTool, string details = "")
        => new()
        {
            Date = new DateOnly(2026, 3, 17),
            LocationName = "Pune",
            Events =
            [
                new AstronomyEventModel
                {
                    ObjectName = "Jupiter",
                    ObservationTool = observationTool,
                    Details = details,
                    VisibilityWindow = "after sunset",
                    Direction = "south",
                    Score = 0.95
                }
            ]
        };

    private static OptimizedVideoMetadata BuildMetadata()
        => new()
        {
            PrimaryTitle = "Tonight's Jupiter Guide",
            OptimizedDescription = "Track Jupiter tonight and compare what you see with nearby stars.",
            Tags = ["astronomy", "jupiter"],
            Hashtags = ["#astronomy", "#jupiter"],
            ThumbnailTextSuggestions = ["See Jupiter Tonight"]
        };
}
