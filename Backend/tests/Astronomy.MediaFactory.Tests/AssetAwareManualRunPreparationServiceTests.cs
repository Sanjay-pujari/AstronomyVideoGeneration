using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed partial class ContentPlanningGeneratePlanTests
{
    [Fact]
    public async Task AssetAwarePackage_Returns_Valid_RunPipelineRequest_And_Separate_Metadata()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentCategoryStyleSettings.Add(new ContentCategoryStyleSettings { ContentCategoryCode = "DailySkyGuide", Language = "en", Enabled = true, HookStyleCode = "HookA", NarrationStyleCode = "NarA", ThumbnailStyleCode = "ThumbA" });
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "ReadyForManualRun", Language = "en", RegionId = "IN-RJ-UDAIPUR", Title = "T", ScheduledUtc = DateTimeOffset.UtcNow };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();

        var service = CreateAssetAwareService(db);
        var before = (await db.ContentGenerationPlans.AsNoTracking().SingleAsync(x => x.Id == plan.Id)).UpdatedUtc;

        var package = await service.PrepareAsync(plan.Id, CancellationToken.None);

        Assert.NotNull(package.RunPipelineRequest);
        Assert.NotNull(package.AssetAwareMetadata);
        Assert.False(ReferenceEquals(package.RunPipelineRequest, package.AssetAwareMetadata));
        Assert.True(package.CanRunManually);
        Assert.Equal(before, (await db.ContentGenerationPlans.AsNoTracking().SingleAsync(x => x.Id == plan.Id)).UpdatedUtc);
        Assert.Empty(db.ContentPipelineExecutions);
    }

    [Fact]
    public async Task AssetAwarePackage_Sets_AssetsReady_True_Only_When_Required_Assets_Exist()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentCategoryStyleSettings.Add(new ContentCategoryStyleSettings { ContentCategoryCode = "DailySkyGuide", Language = "en", Enabled = true, HookStyleCode = "HookA", NarrationStyleCode = "NarA", ThumbnailStyleCode = "ThumbA" });
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", Title = "T", ScheduledUtc = DateTimeOffset.UtcNow };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();

        File.WriteAllText(Path.Combine(dir, $"{plan.Id}_intro-background.png"), "x");
        File.WriteAllText(Path.Combine(dir, $"{plan.Id}_thumbnail-candidate.png"), "x");
        File.WriteAllText(Path.Combine(dir, $"{plan.Id}_supporting-skymap.png"), "x");
        File.WriteAllText(Path.Combine(dir, $"{plan.Id}_outro-background.png"), "x");

        var service = CreateAssetAwareService(db, dir);
        var ready = await service.PrepareAsync(plan.Id, CancellationToken.None);
        Assert.True(ready.AssetsReady);

        File.Delete(Path.Combine(dir, $"{plan.Id}_outro-background.png"));
        var missing = await service.PrepareAsync(plan.Id, CancellationToken.None);
        Assert.False(missing.AssetsReady);
        Assert.True(missing.CanRunManually);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("Skipped")]
    public async Task AssetAwarePackage_Rejects_Terminal_Statuses(string status)
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = status, Language = "en", RegionId = "IN-RJ-UDAIPUR", Title = "T", ScheduledUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var id = db.ContentGenerationPlans.Single().Id;

        var service = CreateAssetAwareService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(id, CancellationToken.None));
    }

    private static IAssetAwareManualRunPreparationService CreateAssetAwareService(MediaFactoryDbContext db, string? captureDir = null)
    {
        var packager = new DailySkyGuideVisualAssetPackager(Options.Create(new StellariumOptions { CaptureDirectory = captureDir ?? Path.GetTempPath() }));
        var contextBuilder = new DailySkyGuideContextBuilder(db, Options.Create(new Astronomy.MediaFactory.Contracts.SchedulerOptions()), new AstronomyVisibilityService(db, new FakeSkyfieldVisibilityClient(), Options.Create(new SkyfieldSidecarOptions())), new StellariumScenePlannerResolver([new DailySkyGuideStellariumScenePlanner()]));
        var resolver = new ContentCategoryPipelineStrategyResolver([new DailySkyGuidePipelineStrategy(packager, contextBuilder)]);
        return new AssetAwareManualRunPreparationService(db, resolver);
    }
}
