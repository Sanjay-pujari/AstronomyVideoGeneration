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
        Assert.Contains("DISCOVERY", newsPlan.PrimaryThumbnailText);
        Assert.Equal(ThumbnailLayoutType.CenteredTitleOverlay, newsPlan.LayoutType);
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
    }
}
