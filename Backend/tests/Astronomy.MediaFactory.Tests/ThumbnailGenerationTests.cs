using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailGenerationTests
{
    [Fact]
    public void ThumbnailStrategy_GeneratesContentSpecificTextAndLayout()
    {
        var service = new ThumbnailStrategyService();
        var context = new AstronomyContext
        {
            Events = [new AstronomyEventModel { ObjectName = "Jupiter", Score = 0.95 }],
            NewsItems = [new NewsItemModel { Headline = "Scientists discover unusual exoplanet", PublishedDate = DateOnly.FromDateTime(DateTime.UtcNow) }]
        };

        var dailyPlan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = context,
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = "."
        });

        var newsPlan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpaceNews,
            Context = context,
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = "."
        });

        Assert.Contains("TONIGHT'S SKY", dailyPlan.PrimaryThumbnailText);
        Assert.Equal(ThumbnailLayoutType.TopBanner, dailyPlan.LayoutType);
        Assert.Equal(dailyPlan.LayoutType, dailyPlan.LayoutCandidates.First());
        Assert.NotEmpty(dailyPlan.Variants);
        Assert.Contains("DISCOVERY", newsPlan.PrimaryThumbnailText);
        Assert.Equal(ThumbnailLayoutType.CenteredTitleOverlay, newsPlan.LayoutType);
        Assert.Equal(newsPlan.LayoutType, newsPlan.LayoutCandidates.First());
    }

    [Fact]
    public void ThumbnailStrategy_UsesFeedbackSignalsToPromoteLayouts()
    {
        var service = new ThumbnailStrategyService();

        var plan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpaceNews,
            Context = new AstronomyContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = ".",
            FeedbackSignals = new FeedbackSignals
            {
                TopKeywords = ["tonight guide", "tonight viewing"]
            }
        });

        Assert.Equal(ThumbnailLayoutType.TopBanner, plan.LayoutType);
        Assert.Equal(3, plan.LayoutCandidates.Count);
    }


    [Fact]
    public void ThumbnailStrategy_UsesExperimentFeedbackHintsForVariants()
    {
        var service = new ThumbnailStrategyService();

        var plan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = new AstronomyContext
            {
                PromptFeedbackContext = new PromptFeedbackContext
                {
                    ThumbnailStrategyHints = ["Recent winning thumbnail pattern: TopBanner: BIG MOON"]
                }
            },
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = "."
        });

        Assert.Contains(plan.AlternateThumbnailTexts, x => x.Contains("BIG MOON", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ThumbnailLayoutType.TopBanner, plan.LayoutCandidates.First());
    }

    [Fact]
    public async Task ThumbnailGeneration_FallsBack_WhenNoVisualsAvailable()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var service = new ThumbnailGenerationService(new ThumbnailStrategyService(), NullLogger<ThumbnailGenerationService>.Instance);
        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.TelescopeTargets,
            Context = new AstronomyContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.NotNull(plan.ThumbnailPath);
        Assert.True(File.Exists(plan.ThumbnailPath));
        Assert.Equal(3, plan.ThumbnailVariantPaths.Count);
        Assert.All(plan.ThumbnailVariantPaths, path => Assert.True(File.Exists(path)));
        Assert.All(plan.ThumbnailVariantPaths, path => Assert.EndsWith(".png", path));
    }

    [Fact]
    public async Task ThumbnailGeneration_ReturnsSourceVisual_WhenCompositionFails()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        var invalidVisual = Path.Combine(outputDir, "not-an-image.png");
        await File.WriteAllTextAsync(invalidVisual, "not-image");

        var service = new ThumbnailGenerationService(new ThumbnailStrategyService(), NullLogger<ThumbnailGenerationService>.Instance);
        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.AstrophotographyTips,
            Context = new AstronomyContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [invalidVisual],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.Equal(invalidVisual, plan.ThumbnailPath);
        Assert.Single(plan.ThumbnailVariantPaths);
    }

    [Fact]
    public void ThumbnailStrategy_GeneratesSpecialEventText()
    {
        var service = new ThumbnailStrategyService();
        var plan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = new AstronomyContext
            {
                SpecialEvent = new SpecialEventContext
                {
                    EventId = "moon-full-moon-20260504",
                    EventType = "full_moon",
                    EventTitle = "Full Moon Tonight",
                    EventDescription = "Full moon event."
                }
            },
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.GetTempPath()
        });

        Assert.Contains("FULL MOON", plan.PrimaryThumbnailText);
        Assert.Equal(ThumbnailLayoutType.CenteredTitleOverlay, plan.LayoutType);
    }

}
