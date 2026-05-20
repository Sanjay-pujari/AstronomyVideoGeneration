using Astronomy.MediaFactory.AIOptimization;
using Astronomy.MediaFactory.Analytics;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Analytics;
using Astronomy.MediaFactory.Infrastructure.Optimization;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AiAnalyticsUtcPersistenceTests
{
    [Fact]
    public async Task AIOptimization_AsiaKolkata_Run_Saves_Utc_RecommendedPublishTime()
    {
        await using var db = CreateDb();
        var service = new AIOptimizationPipelineService(
            db,
            new HookOptimizationService(),
            new PublishingOptimizationService(),
            Options.Create(new AIOptimizationOptions { Enabled = true }));

        var runDateIst = new DateTimeOffset(2026, 5, 19, 22, 30, 0, TimeSpan.FromHours(5.5));
        var output = Path.Combine(Path.GetTempPath(), $"ai-opt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);

        await service.RunForPipelineAsync(new AIOptimizationPipelineRequest(
            Guid.NewGuid(),
            output,
            "en",
            "in",
            DateOnly.FromDateTime(runDateIst.DateTime),
            "Udaipur",
            "Jupiter is bright tonight",
            "Jupiter over Udaipur",
            ["Jupiter"],
            null,
            null,
            "planetary"), CancellationToken.None);

        var saved = await db.PublishingOptimizationResults.SingleAsync();
        Assert.Equal(TimeSpan.Zero, saved.RecommendedPublishTime.Offset);
    }

    [Fact]
    public async Task AnalyticsInitialization_Converts_PublishedAtUtc_To_Utc()
    {
        await using var db = CreateDb();
        var service = new ManualAnalyticsIngestionService(db, NullLogger<ManualAnalyticsIngestionService>.Instance);
        var runId = Guid.NewGuid();
        var publishedIst = new DateTimeOffset(2026, 5, 19, 18, 0, 0, TimeSpan.FromHours(5.5));

        await service.InitializeForPipelineRunAsync(new AnalyticsPipelineInitializationRequest(
            runId,
            "en",
            "in",
            publishedIst,
            ["YouTube"],
            ["Hook"],
            [new AnalyticsThumbnailSeed("/tmp/thumb.jpg", "Long")],
            "Short",
            "yt123",
            null), CancellationToken.None);

        var video = await db.PlatformVideoAnalytics.SingleAsync();
        var hook = await db.HookPerformance.SingleAsync();
        var thumb = await db.ThumbnailPerformance.SingleAsync();

        Assert.Equal(TimeSpan.Zero, video.PublishedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, hook.PublishedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, thumb.PublishedAtUtc.Offset);
    }

    [Fact]
    public async Task AnalyticsInitialization_Creates_Zero_Metric_Baseline_Rows()
    {
        await using var db = CreateDb();
        var service = new ManualAnalyticsIngestionService(db, NullLogger<ManualAnalyticsIngestionService>.Instance);
        var runId = Guid.NewGuid();

        await service.InitializeForPipelineRunAsync(new AnalyticsPipelineInitializationRequest(
            runId,
            "en",
            "us",
            DateTimeOffset.UtcNow,
            ["YouTube-Long", "YouTube-Short"],
            ["Hook A", "Hook B"],
            [new AnalyticsThumbnailSeed("/tmp/long.jpg", "Long"), new AnalyticsThumbnailSeed("/tmp/short.jpg", "Short")],
            "Short",
            "yt123",
            null), CancellationToken.None);

        Assert.Equal(2, await db.PlatformVideoAnalytics.CountAsync(x => x.PipelineRunId == runId));
        Assert.Equal(2, await db.PlatformContentAnalytics.CountAsync(x => x.PipelineRunId == runId));
        Assert.Equal(4, await db.HookPerformance.CountAsync(x => x.PipelineRunId == runId));
        Assert.Equal(4, await db.ThumbnailPerformance.CountAsync(x => x.PipelineRunId == runId));
        Assert.All(await db.ThumbnailPerformance.Where(x => x.PipelineRunId == runId).ToListAsync(), x => Assert.False(string.IsNullOrWhiteSpace(x.ThumbnailPath)));
        Assert.All(await db.PlatformVideoAnalytics.Where(x => x.PipelineRunId == runId).ToListAsync(), x => Assert.Equal(0, x.Views));

        await service.InitializeForPipelineRunAsync(new AnalyticsPipelineInitializationRequest(
            runId,
            "en",
            "us",
            DateTimeOffset.UtcNow,
            ["YouTube-Long", "YouTube-Short"],
            ["Hook A", "Hook B"],
            [new AnalyticsThumbnailSeed("/tmp/long.jpg", "Long"), new AnalyticsThumbnailSeed("/tmp/short.jpg", "Short")],
            "Short",
            "yt123",
            null), CancellationToken.None);

        Assert.Equal(2, await db.PlatformVideoAnalytics.CountAsync(x => x.PipelineRunId == runId));
        Assert.Equal(2, await db.PlatformContentAnalytics.CountAsync(x => x.PipelineRunId == runId));
        Assert.Equal(4, await db.HookPerformance.CountAsync(x => x.PipelineRunId == runId));
        Assert.Equal(4, await db.ThumbnailPerformance.CountAsync(x => x.PipelineRunId == runId));
    }

    [Fact]
    public async Task SaveChanges_Guard_Normalizes_Any_Utc_Suffixed_DateTimeOffset()
    {
        await using var db = CreateDb();
        db.PlatformVideoAnalytics.Add(new PlatformVideoAnalytics
        {
            PipelineRunId = Guid.NewGuid(),
            Platform = "YouTube",
            ContentType = "Short",
            Language = "en",
            RegionId = "in",
            PublishedAtUtc = new DateTimeOffset(2026, 5, 19, 10, 0, 0, TimeSpan.FromHours(5.5))
        });

        await db.SaveChangesAsync();

        var row = await db.PlatformVideoAnalytics.SingleAsync();
        Assert.Equal(TimeSpan.Zero, row.PublishedAtUtc.Offset);
    }

    private static MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new MediaFactoryDbContext(options);
    }
}
