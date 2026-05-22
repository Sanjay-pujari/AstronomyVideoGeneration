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
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Language = "en", RegionId = "r" };
        db.ContentGenerationPlans.Add(plan);
        var planId = plan.Id;
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
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Language = "en", RegionId = "r" };
        db.ContentGenerationPlans.Add(plan);
        var planId = plan.Id;
        await db.SaveChangesAsync();
        var before = db.ChangeTracker.Entries().Count();

        var generator = BuildGenerator(db, new FakePlanner(planId));
        var result = await generator.GenerateAsync(planId, new AssetAwarePreviewVideoRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(before, db.ChangeTracker.Entries().Count());
        Assert.Contains(Path.Combine("content-plans", planId.ToString("D"), "preview-videos"), result.OutputVideoPath!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("content-plans", planId.ToString("D"), "thumbnails"), result.ThumbnailPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewInfo_UsesRenderingWorkingDirectory()
    {
        await using var db = BuildDb();
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Language = "en", RegionId = "r" };
        db.ContentGenerationPlans.Add(plan);
        var planId = plan.Id;
        await db.SaveChangesAsync();

        var renderingDirectory = Path.Combine(Path.GetTempPath(), "media-output");
        var generator = new DailySkyGuidePreviewVideoGenerator(
            db,
            new FakePlanner(planId),
            new FakeComposer(),
            Options.Create(new RenderingOptions { WorkingDirectory = renderingDirectory }));

        var info = await generator.GetPreviewInfoAsync(planId, CancellationToken.None);
        Assert.StartsWith(Path.Combine(renderingDirectory, "content-plans", planId.ToString("D"), "preview-videos"), info.OutputFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static DailySkyGuidePreviewVideoGenerator BuildGenerator(MediaFactoryDbContext db, IDailySkyGuideAssetAwareCompositionPlanner planner)
        => new(db, planner, new FakeComposer(), Options.Create(new RenderingOptions { WorkingDirectory = Path.GetTempPath() }));

    private static MediaFactoryDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private sealed class FakeComposer : IAssetAwarePreviewVideoComposer
    {
        public Task<AssetAwarePreviewVideoComposeResult> ComposeAsync(AssetAwareVideoCompositionPlan plan, AssetAwarePreviewVideoRequest request, string outputVideoPath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputVideoPath)!);
            File.WriteAllText(outputVideoPath, "x");
            var planRoot = Directory.GetParent(Path.GetDirectoryName(outputVideoPath)!)?.FullName ?? Path.GetDirectoryName(outputVideoPath)!;
            var thumbnailPath = Path.Combine(planRoot, "thumbnails", "daily-skyguide-preview-thumbnail.png");
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            File.WriteAllText(thumbnailPath, "x");
            return Task.FromResult(new AssetAwarePreviewVideoComposeResult(outputVideoPath, thumbnailPath, "ffmpeg ...", 0, string.Empty, string.Empty, "ffmpeg"));
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
