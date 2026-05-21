using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CategoryRequirementAndVisualStrategyTests
{
    [Fact]
    public async Task DailySkyGuide_Requires_Skyfield_Stellarium_And_Ssc()
    {
        var resolver = new CategoryRequirementResolver();

        var requirement = await resolver.ResolveAsync("DailySkyGuide", CancellationToken.None);

        Assert.True(requirement.RequiresSkyfield);
        Assert.True(requirement.RequiresStellarium);
        Assert.True(requirement.RequiresSscScript);
    }

    [Fact]
    public async Task CosmicStoryShort_Does_Not_Require_Skyfield_Or_Stellarium()
    {
        var resolver = new CategoryRequirementResolver();

        var requirement = await resolver.ResolveAsync("CosmicStoryShort", CancellationToken.None);

        Assert.False(requirement.RequiresSkyfield);
        Assert.False(requirement.RequiresStellarium);
        Assert.True(requirement.RequiresAiImages);
    }

    [Fact]
    public async Task VisualStrategyPreview_Is_Decision_Only_And_Does_Not_Generate_Assets()
    {
        var categoryResolver = new CategoryRequirementResolver();
        var visualResolver = new VisualStrategyResolver(categoryResolver);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide" };

        var strategy = await visualResolver.ResolveAsync(plan, CancellationToken.None);

        Assert.True(strategy.UseStellariumCapture);
        Assert.True(strategy.UseSscScript);
        Assert.False(strategy.UseAiImageGeneration);
        Assert.Contains("StellariumSceneImages", strategy.AssetTypesToGenerate);
        Assert.Contains("ThumbnailCandidate", strategy.AssetTypesToGenerate);
    }

    [Fact]
    public async Task VisualStrategyPreview_Does_Not_Call_Pipeline()
    {
        var categoryResolver = new CategoryRequirementResolver();
        var visualResolver = new VisualStrategyResolver(categoryResolver);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "CosmicStoryShort" };

        var strategy = await visualResolver.ResolveAsync(plan, CancellationToken.None);

        Assert.False(strategy.UseStellariumCapture);
        Assert.False(strategy.UseSscScript);
        Assert.True(strategy.UseAiImageGeneration);
        Assert.Contains("CinematicAiImages", strategy.AssetTypesToGenerate);
        Assert.Contains("ThumbnailCandidate", strategy.AssetTypesToGenerate);
    }

    [Fact]
    public void Pipeline_Run_Endpoint_Remains_Unchanged()
    {
        var programSource = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "Astronomy.MediaFactory.Api", "Program.cs"));
        Assert.Contains("/api/pipeline/run", programSource);
    }
}
