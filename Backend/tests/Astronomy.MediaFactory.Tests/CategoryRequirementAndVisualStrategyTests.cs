using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CategoryRequirementAndVisualStrategyTests
{
    [Fact]
    public async Task DailySkyGuide_Requirement_Has_Expected_Flags()
    {
        var requirement = await new CategoryRequirementResolver().ResolveAsync("DailySkyGuide", CancellationToken.None);
        Assert.True(requirement.RequiresSkyfield);
        Assert.True(requirement.RequiresStellarium);
        Assert.True(requirement.RequiresSscScript);
        Assert.True(requirement.RequiresVoiceNarration);
        Assert.True(requirement.RequiresThumbnail);
    }

    [Fact]
    public async Task CosmicStoryShort_Requirement_Has_Expected_Flags()
    {
        var requirement = await new CategoryRequirementResolver().ResolveAsync("CosmicStoryShort", CancellationToken.None);
        Assert.False(requirement.RequiresSkyfield);
        Assert.False(requirement.RequiresStellarium);
        Assert.False(requirement.RequiresSscScript);
        Assert.True(requirement.RequiresAiImages);
        Assert.True(requirement.RequiresNasaImages);
    }

    [Fact]
    public async Task MythologySkyStory_Requirement_Has_Expected_Flags()
    {
        var requirement = await new CategoryRequirementResolver().ResolveAsync("MythologySkyStory", CancellationToken.None);
        Assert.True(requirement.RequiresAiImages);
        Assert.False(requirement.RequiresStellarium);
    }

    [Fact]
    public async Task AstroPhotographyGuide_Requirement_Has_Expected_Flags()
    {
        var requirement = await new CategoryRequirementResolver().ResolveAsync("AstroPhotographyGuide", CancellationToken.None);
        Assert.True(requirement.RequiresSkyfield);
        Assert.True(requirement.RequiresStellarium);
        Assert.True(requirement.RequiresEducationalDiagrams);
    }

    [Fact]
    public async Task Unsupported_Category_Returns_Warning_Without_Crash()
    {
        var requirement = await new CategoryRequirementResolver().ResolveAsync("NoSuchCategory", CancellationToken.None);
        Assert.False(requirement.RequiresSkyfield);
        Assert.Contains("No category requirement definition found for this content category.", requirement.Warnings);
    }

    [Fact]
    public async Task Visual_Strategy_DailySkyGuide_Expected_Flags()
    {
        var strategy = await new VisualStrategyResolver(new CategoryRequirementResolver())
            .ResolveAsync(new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide" }, CancellationToken.None);
        Assert.True(strategy.UseStellariumCapture);
        Assert.True(strategy.UseSscScript);
        Assert.False(strategy.UseAiImageGeneration);
    }

    [Fact]
    public async Task Visual_Strategy_CosmicStoryShort_Expected_Flags()
    {
        var strategy = await new VisualStrategyResolver(new CategoryRequirementResolver())
            .ResolveAsync(new ContentGenerationPlan { ContentCategoryCode = "CosmicStoryShort" }, CancellationToken.None);
        Assert.False(strategy.UseStellariumCapture);
        Assert.False(strategy.UseSscScript);
        Assert.True(strategy.UseAiImageGeneration);
    }

    [Fact]
    public async Task Visual_Strategy_Preview_Does_Not_Update_Database()
    {
        await using var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", ScheduledUtc = DateTimeOffset.UtcNow };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var before = (await db.ContentGenerationPlans.SingleAsync(x => x.Id == plan.Id)).UpdatedUtc;

        _ = await new VisualStrategyResolver(new CategoryRequirementResolver()).ResolveAsync(plan, CancellationToken.None);

        var after = (await db.ContentGenerationPlans.SingleAsync(x => x.Id == plan.Id)).UpdatedUtc;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Visual_Strategy_Preview_Does_Not_Call_Pipeline()
    {
        await using var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", ScheduledUtc = DateTimeOffset.UtcNow };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();

        _ = await new VisualStrategyResolver(new CategoryRequirementResolver()).ResolveAsync(plan, CancellationToken.None);

        Assert.Empty(db.ContentPipelineExecutions);
    }

    [Fact]
    public void Pipeline_Run_Endpoint_Remains_Unchanged()
    {
        var programSource = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "Astronomy.MediaFactory.Api", "Program.cs"));
        Assert.Contains("/api/pipeline/run", programSource);
    }
}
