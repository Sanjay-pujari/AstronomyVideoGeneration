using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class DailySkyGuidePreviewVideoGeneratorTests
{
    [Fact]
    public async Task PreviewInfo_SkipsMissingAssets_AndPreservesOrder()
    {
        await using var db = BuildDb();
        var planId = Guid.NewGuid();
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { Id = planId, ContentCategoryCode = "DailySkyGuide", Language = "en", RegionId = "r" });
        await db.SaveChangesAsync();

        var generator = BuildGenerator(db, new FakePlanner(planId));
        var info = await generator.GetPreviewInfoAsync(planId, CancellationToken.None);

        Assert.True(info.Success);
        Assert.Equal(2, info.SegmentCount);
        Assert.Equal(new[] { 1, 2, 3 }, info.Segments.Select(x => x.SortOrder));
        Assert.False(info.Segments[1].IncludedInVideo);
    }

    [Fact]
    public async Task Generate_DoesNotTouchDbState()
    {
        await using var db = BuildDb();
        var planId = Guid.NewGuid();
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { Id = planId, ContentCategoryCode = "DailySkyGuide", Language = "en", RegionId = "r" });
        await db.SaveChangesAsync();
        var before = db.ChangeTracker.Entries().Count();

        var generator = BuildGenerator(db, new FakePlanner(planId));
        var result = await generator.GenerateAsync(planId, new AssetAwarePreviewVideoRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(before, db.ChangeTracker.Entries().Count());
    }

    private static DailySkyGuidePreviewVideoGenerator BuildGenerator(MediaFactoryDbContext db, IDailySkyGuideAssetAwareCompositionPlanner planner)
        => new(db, planner, new FakeComposer(), Options.Create(new StellariumOptions { OutputRoot = Path.GetTempPath(), CaptureDirectory = Path.GetTempPath() }));

    private static MediaFactoryDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private sealed class FakeComposer : IAssetAwarePreviewVideoComposer
    {
        public Task<string?> ComposeAsync(AssetAwareVideoCompositionPlan plan, AssetAwarePreviewVideoRequest request, string outputVideoPath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputVideoPath)!);
            File.WriteAllText(outputVideoPath, "x");
            return Task.FromResult<string?>(outputVideoPath);
        }
    }

    private sealed class FakePlanner(Guid planId) : IDailySkyGuideAssetAwareCompositionPlanner
    {
        public Task<AssetAwareVideoCompositionPlan> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
            => Task.FromResult(new AssetAwareVideoCompositionPlan(planId, "DailySkyGuide", "Loc", new DateOnly(2026, 5, 21), "en", "t", 3,
            [
                new AssetAwareVideoSegment(1,"A","Intro","IntroBackground","/tmp/a.png",true,null,6,"FadeIn",null),
                new AssetAwareVideoSegment(2,"B","Body","SupportingSkyMap",null,false,null,8,"CrossFade",null),
                new AssetAwareVideoSegment(3,"C","Outro","OutroBackground","/tmp/c.png",true,null,5,"FadeOut",null)
            ], [], false));
    }
}
