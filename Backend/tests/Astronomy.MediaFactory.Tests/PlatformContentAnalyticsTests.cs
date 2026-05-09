using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Analytics;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PlatformContentAnalyticsTests
{
    [Fact]
    public async Task YouTubeAnalyticsCollector_CollectsMetricsSafely()
    {
        var collector = new YouTubeAnalyticsCollector(new FakeYouTubeAnalyticsService());
        var result = await collector.CollectAsync(Context("YouTube", "Short", "yt1"), CancellationToken.None);

        Assert.True(result.IsAnalyticsAvailable);
        Assert.Equal(123, result.Views);
        Assert.Equal(12, result.Likes);
        Assert.Equal(3, result.Comments);
        Assert.Equal(42, result.AverageViewDurationSeconds);
    }

    [Fact]
    public async Task FacebookAnalyticsCollector_MissingPermissionsHandledSafely()
    {
        var collector = new FacebookAnalyticsCollector(new HttpClient(), Options.Create(new MetaOptions { TokenFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }), NullLogger<FacebookAnalyticsCollector>.Instance);
        var result = await collector.CollectAsync(Context("Facebook", "Reel", "fb1"), CancellationToken.None);

        Assert.False(result.IsAnalyticsAvailable);
        Assert.Contains("token", result.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstagramAnalyticsCollector_MissingPermissionsHandledSafely()
    {
        var collector = new InstagramAnalyticsCollector(new HttpClient(), Options.Create(new MetaOptions { TokenFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }), NullLogger<InstagramAnalyticsCollector>.Instance);
        var result = await collector.CollectAsync(Context("Instagram", "Reel", "ig1"), CancellationToken.None);

        Assert.False(result.IsAnalyticsAvailable);
        Assert.Contains("token", result.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicatePlatformAnalytics_UpdatesInsteadOfDuplicateInsert()
    {
        await using var db = CreateDb();
        var repo = new EfPipelineRepository(db);
        var collected = DateTimeOffset.UtcNow;
        await repo.UpsertPlatformContentAnalyticsAsync(new PlatformContentAnalytics { Platform = "YouTube", PlatformContentType = "Short", PlatformMediaId = "yt1", CollectedUtc = collected, Views = 1 }, CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);
        await repo.UpsertPlatformContentAnalyticsAsync(new PlatformContentAnalytics { Platform = "YouTube", PlatformContentType = "Short", PlatformMediaId = "yt1", CollectedUtc = collected, Views = 99 }, CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);

        Assert.Single(db.PlatformContentAnalytics);
        Assert.Equal(99, db.PlatformContentAnalytics.Single().Views);
    }

    [Fact]
    public async Task AnalyticsCollectionService_CollectsRecentContentAndWritesReport()
    {
        await using var db = CreateDb();
        var output = Path.Combine(Path.GetTempPath(), "analytics-tests", Guid.NewGuid().ToString("N"));
        var run = SeedPublishedRun(db, output);
        await db.SaveChangesAsync();
        var service = CreateCollectionService(db, new SuccessCollector("YouTube"), new SuccessCollector("Facebook"), new SuccessCollector("Instagram"));

        await service.CollectForPipelineRunAsync(run.Id, CancellationToken.None);

        Assert.True(db.PlatformContentAnalytics.Count() >= 3);
        Assert.True(File.Exists(Path.Combine(output, "analytics-collection-report.json")));
    }

    [Fact]
    public async Task BackgroundCollectionScheduler_RunsWithoutBreakingOnFailures()
    {
        var fake = new FakeAnalyticsCollectionService { Throw = true };
        var services = new ServiceCollection();
        services.AddSingleton<IAnalyticsCollectionService>(fake);
        using var provider = services.BuildServiceProvider();
        var background = new AnalyticsCollectionBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), new FakeOptionsMonitor(new AnalyticsOptions { Enabled = true, CollectEveryMinutes = 1 }), NullLogger<AnalyticsCollectionBackgroundService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await background.StartAsync(cts.Token);
        await Task.Delay(50);
        await background.StopAsync(CancellationToken.None);

        Assert.True(fake.Calls >= 1);
    }

    [Fact]
    public async Task DashboardSummary_AggregatesMetrics()
    {
        await using var db = CreateDb();
        var repo = new EfPipelineRepository(db);
        await repo.UpsertPlatformContentAnalyticsAsync(new PlatformContentAnalytics { Platform = "YouTube", PlatformContentType = "Short", PlatformMediaId = "yt1", CollectedUtc = DateTimeOffset.UtcNow, PublishedUtc = DateTimeOffset.UtcNow.Date.AddHours(18), Views = 100, Likes = 10, Comments = 3, Shares = 2, IsAnalyticsAvailable = true }, CancellationToken.None);
        await repo.UpsertPlatformContentAnalyticsAsync(new PlatformContentAnalytics { Platform = "Instagram", PlatformContentType = "Reel", PlatformMediaId = "ig1", CollectedUtc = DateTimeOffset.UtcNow, Views = 200, Likes = 50, Comments = 5, Shares = 5, IsAnalyticsAvailable = true }, CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);

        var summary = await repo.GetAnalyticsDashboardSummaryAsync(14, CancellationToken.None);

        Assert.Equal(300, summary.TotalViews);
        Assert.Equal(75, summary.TotalEngagement);
        Assert.Equal("Instagram", summary.BestPerformingPlatform);
        Assert.NotNull(summary.BestPerformingReel);
        Assert.Equal(18, summary.BestPerformingPublishHourUtc);
    }

    [Fact]
    public async Task AnalyticsFailures_DoNotMarkPipelineFailed()
    {
        await using var db = CreateDb();
        var output = Path.Combine(Path.GetTempPath(), "analytics-tests", Guid.NewGuid().ToString("N"));
        var run = SeedPublishedRun(db, output);
        await db.SaveChangesAsync();
        var service = CreateCollectionService(db, new ThrowingCollector("YouTube"));

        await service.CollectForPipelineRunAsync(run.Id, CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, db.PipelineRuns.Single().Status);
        Assert.Contains(db.PlatformContentAnalytics, x => !x.IsAnalyticsAvailable);
    }

    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static PipelineRun SeedPublishedRun(MediaFactoryDbContext db, string output)
    {
        Directory.CreateDirectory(output);
        var run = new PipelineRun { RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.SpaceNews, LocationName = "Austin", Status = PipelineRunStatus.Succeeded, FinishedUtc = DateTimeOffset.UtcNow, OutputFolder = output };
        var video = new PublishedVideo { PipelineRunId = run.Id, Title = "Sky", YouTubeVideoId = "yt-long", CreatedAt = DateTimeOffset.UtcNow };
        var shortVideo = new ShortVideo { ParentVideoId = video.Id, YouTubeVideoId = "yt-short", Duration = 30 };
        db.PipelineRuns.Add(run);
        db.PublishedVideos.Add(video);
        db.ShortVideos.Add(shortVideo);
        db.PlatformPublicationRecords.Add(new PlatformPublicationRecord { ParentShortVideoId = shortVideo.Id, Platform = ShortFormPlatform.Facebook, ExternalPostId = "fb1", ExternalUrl = "https://facebook.example/fb1", Status = PlatformPublicationStatus.Published, PublishedAt = DateTimeOffset.UtcNow });
        db.PlatformPublicationRecords.Add(new PlatformPublicationRecord { ParentShortVideoId = shortVideo.Id, Platform = ShortFormPlatform.InstagramReels, ExternalPostId = "ig1", ExternalUrl = "https://instagram.example/ig1", Status = PlatformPublicationStatus.Published, PublishedAt = DateTimeOffset.UtcNow });
        return run;
    }

    private static AnalyticsCollectionService CreateCollectionService(MediaFactoryDbContext db, params IPlatformAnalyticsCollector[] collectors)
        => new(db, new EfPipelineRepository(db), collectors, Options.Create(new AnalyticsOptions { CollectForRecentDays = 14, CollectEveryMinutes = 60 }), Options.Create(new MaintenanceOptions { WorkingDirectory = Path.GetTempPath() }), NullLogger<AnalyticsCollectionService>.Instance);

    private static PlatformAnalyticsCollectionContext Context(string platform, string type, string id)
        => new(Guid.NewGuid(), platform, type, id, null, "Title", DateTimeOffset.UtcNow, 30, "#space", null, null, "Austin", DateOnly.FromDateTime(DateTime.UtcNow), ContentType.SpaceNews, null, null);

    private sealed class FakeYouTubeAnalyticsService : IYouTubeAnalyticsService
    {
        public Task<YouTubeVideoAnalyticsSnapshot?> GetVideoAnalyticsAsync(string videoId, CancellationToken cancellationToken)
            => Task.FromResult<YouTubeVideoAnalyticsSnapshot?>(new YouTubeVideoAnalyticsSnapshot { VideoId = videoId, Views = 123, Likes = 12, Comments = 3, DurationSeconds = 60, AverageViewDurationSeconds = 42, EstimatedMinutesWatched = 10 });
    }

    private sealed class SuccessCollector : IPlatformAnalyticsCollector
    {
        public string Platform { get; }
        public SuccessCollector(string platform) => Platform = platform;
        public Task<PlatformContentAnalytics> CollectAsync(PlatformAnalyticsCollectionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new PlatformContentAnalytics { PipelineRunId = context.PipelineRunId, Platform = context.Platform, PlatformContentType = context.PlatformContentType, PlatformMediaId = context.PlatformMediaId, CollectedUtc = DateTimeOffset.UtcNow, Views = 10, Likes = 1, IsAnalyticsAvailable = true });
    }

    private sealed class ThrowingCollector : IPlatformAnalyticsCollector
    {
        public string Platform { get; }
        public ThrowingCollector(string platform) => Platform = platform;
        public Task<PlatformContentAnalytics> CollectAsync(PlatformAnalyticsCollectionContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("analytics denied");
    }

    private sealed class FakeAnalyticsCollectionService : IAnalyticsCollectionService
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }
        public Task CollectRecentAnalyticsAsync(CancellationToken cancellationToken) { Calls++; if (Throw) throw new InvalidOperationException("boom"); return Task.CompletedTask; }
        public Task CollectForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeOptionsMonitor : IOptionsMonitor<AnalyticsOptions>
    {
        public FakeOptionsMonitor(AnalyticsOptions value) => CurrentValue = value;
        public AnalyticsOptions CurrentValue { get; }
        public AnalyticsOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AnalyticsOptions, string?> listener) => null;
    }
}
