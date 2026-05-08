using System.Net;
using System.Net.Http.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Operations;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class OpsDashboardServiceTests
{
    [Fact]
    public async Task Dashboard_ReturnsRecentRuns_FailedStage_PublishedUrls_TokenHealth_AndSanitizedSystemHealth()
    {
        await using var db = CreateDb();
        var run = new PipelineRun
        {
            RunDate = new DateOnly(2026, 5, 8),
            ContentType = ContentType.DailySkyGuide,
            LocationName = "Udaipur, India",
            TimeZone = "Asia/Kolkata",
            Status = PipelineRunStatus.Failed,
            StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
            FinishedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            FailureReason = "Pipeline failed"
        };
        db.PipelineRuns.Add(run);
        db.PipelineStageExecutions.AddRange(
            new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.RenderingCompleted, Status = PipelineStageStatuses.Failed, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-16), FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-15), DurationMs = 60000, ErrorMessage = "render failed" },
            new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.YouTubeLongPublished, Status = PersistentStageStatuses.Succeeded, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-14), FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-13), DurationMs = 1000 });
        var published = new PublishedVideo { PipelineRunId = run.Id, Title = "Daily Sky", Status = "Published", YouTubeVideoId = "abc123", BlobUrl = "https://storage.example/video.mp4?sig=secret" };
        db.PublishedVideos.Add(published);
        var shortVideo = new ShortVideo { ParentVideoId = published.Id, YouTubeVideoId = "short123" };
        db.ShortVideos.Add(shortVideo);
        db.PlatformPublicationRecords.AddRange(
            new PlatformPublicationRecord { ParentShortVideoId = shortVideo.Id, Platform = ShortFormPlatform.YouTubeShorts, Status = PlatformPublicationStatus.Published, ExternalUrl = "https://youtube.com/shorts/short123?sig=secret" },
            new PlatformPublicationRecord { ParentShortVideoId = shortVideo.Id, Platform = ShortFormPlatform.Facebook, Status = PlatformPublicationStatus.Published, ExternalUrl = "https://facebook.example/reel/1?access_token=secret" },
            new PlatformPublicationRecord { ParentShortVideoId = shortVideo.Id, Platform = ShortFormPlatform.InstagramReels, Status = PlatformPublicationStatus.Published, ExternalUrl = "https://instagram.example/reel/1?token=secret" });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var dashboard = await service.GetDashboardAsync(CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(dashboard);

        Assert.Contains(dashboard.RecentPipelineRuns, x => x.RunId == run.Id);
        Assert.Contains(dashboard.RecentPipelineRuns, x => x.RunId == run.Id && x.FailedStage == PipelineStageNames.RenderingCompleted && x.LastError == "render failed");
        Assert.Equal("https://www.youtube.com/watch?v=abc123", dashboard.PlatformPublishSummary.YouTubeLong.Url);
        Assert.Equal("https://youtube.com/shorts/short123", dashboard.PlatformPublishSummary.YouTubeShort.Url);
        Assert.True(dashboard.TokenHealthSummary.YouTubeValid);
        Assert.True(dashboard.TokenHealthSummary.MetaValid);
        Assert.True(dashboard.SystemHealthSummary.AzureBlobStorageConfigured);
        Assert.DoesNotContain("AccountKey=secret", json);
        Assert.DoesNotContain("sig=secret", json);
        Assert.DoesNotContain("access_token=secret", json);
    }

    [Fact]
    public async Task RunAndFailuresEndpoints_ReturnFailures_And_MissingDiagnosticsWarning()
    {
        await using var db = CreateDb();
        var run = new PipelineRun
        {
            RunDate = new DateOnly(2026, 5, 8),
            ContentType = ContentType.TelescopeTargets,
            LocationName = "Austin, TX",
            Status = PipelineRunStatus.Failed,
            StartedUtc = DateTimeOffset.UtcNow.AddHours(-2),
            FinishedUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };
        db.PipelineRuns.Add(run);
        db.PipelineStageExecutions.Add(new PipelineStageExecution { PipelineRunId = run.Id, StageName = "Narration", Status = PipelineStageStatuses.Failed, StartedAt = DateTimeOffset.UtcNow.AddHours(-2), ErrorMessage = "bad script" });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var filteredRuns = await service.GetRunsAsync(new DateOnly(2026, 5, 8), "failed", CancellationToken.None);
        var detail = await service.GetRunAsync(run.Id, CancellationToken.None);
        var failures = await service.GetFailuresAsync(7, CancellationToken.None);
        var dashboard = await service.GetDashboardAsync(CancellationToken.None);

        Assert.Single(filteredRuns);
        Assert.Equal("Narration", detail!.Run.FailedStage);
        Assert.Equal("Narration", failures.MostCommonFailedStage);
        Assert.True(failures.FailuresLast24Hours >= 1);
        Assert.False(dashboard.Diagnostics.Exists);
        Assert.Contains(dashboard.Warnings, x => x.Contains("ops-dashboard.json", StringComparison.OrdinalIgnoreCase));
    }

    private static MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private static OpsDashboardService CreateService(MediaFactoryDbContext db)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "ops-dashboard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "probe.txt"), "ok");

        return new OpsDashboardService(
            db,
            new FakeScheduler(),
            new FakeTokenHealth(),
            Options.Create(new RenderingOptions { FfmpegPath = "ffmpeg" }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workingDirectory }),
            Options.Create(new StellariumOptions { ScriptsDirectory = workingDirectory }),
            Options.Create(new SkyfieldSidecarOptions { Enabled = true, BaseUrl = "http://skyfield.local" }),
            Options.Create(new AzureBlobOptions { ConnectionString = "AccountName=test;AccountKey=secret" }),
            new HttpClient(new OkHandler()));
    }

    private sealed class FakeScheduler : IPipelineSchedulerService
    {
        public Task EvaluateSchedulesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecoverStartupAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<SchedulerStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new SchedulerStatusResponse(
                true,
                1,
                0,
                0,
                [new SchedulerScheduleStatus("daily", true, "Udaipur", 0, 0, "UTC", "18:00", true, DateTimeOffset.UtcNow.AddHours(1), DateOnly.FromDateTime(DateTime.UtcNow))],
                [new SchedulerRunRecord("daily", DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(-1), Guid.NewGuid(), "Succeeded", null, "Udaipur", "UTC", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow)]));
        public Task<SchedulerRunResult> RunNowAsync(string scheduleName, bool force, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> EnableScheduleAsync(string scheduleName, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> DisableScheduleAsync(string scheduleName, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeTokenHealth : ITokenHealthService
    {
        public Task<IReadOnlyList<TokenHealthResult>> CheckAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TokenHealthResult>>([
                new TokenHealthResult { Platform = "YouTube", IsConfigured = true, IsValid = true, DaysUntilExpiry = 30 },
                new TokenHealthResult { Platform = "Meta", IsConfigured = true, IsValid = true, DaysUntilExpiry = 30 }
            ]);
        public Task<TokenHealthResult> CheckYouTubeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TokenHealthResult> CheckMetaAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
