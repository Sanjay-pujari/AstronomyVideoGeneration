using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Operations;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class RecoveryOperationsTests
{
    [Fact]
    public void ReplayRequestValidation_RejectsMissingRequester()
    {
        var error = RecoveryRequestValidator.Validate(new ReplayPipelineRequest(""));
        Assert.Equal("RequestedBy is required.", error);
    }

    [Fact]
    public async Task RecoverStaleJobs_MarksAndRequeuesCandidates_WithoutTouchingSuccessfulJobs()
    {
        await using var db = CreateDb();
        var run = new PipelineRun { RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.DailySkyGuide, LocationName = "Pune", Status = PipelineRunStatus.Succeeded };
        var staleRunning = new PipelineJob { ParentPipelineRunId = run.Id, JobType = PipelineJobType.PublishVideo, RunDate = run.RunDate, ContentType = run.ContentType, LocationName = run.LocationName, Status = PipelineJobStatus.Running, StartedAt = DateTimeOffset.UtcNow.AddHours(-3), ScheduledAt = DateTimeOffset.UtcNow.AddHours(-3) };
        var stalePending = new PipelineJob { ParentPipelineRunId = run.Id, JobType = PipelineJobType.ArchiveAssets, RunDate = run.RunDate, ContentType = run.ContentType, LocationName = run.LocationName, Status = PipelineJobStatus.Pending, ScheduledAt = DateTimeOffset.UtcNow.AddHours(-2) };
        var succeeded = new PipelineJob { ParentPipelineRunId = run.Id, JobType = PipelineJobType.GenerateShorts, RunDate = run.RunDate, ContentType = run.ContentType, LocationName = run.LocationName, Status = PipelineJobStatus.Succeeded, ScheduledAt = DateTimeOffset.UtcNow.AddHours(-2) };
        db.PipelineRuns.Add(run);
        db.PipelineJobs.AddRange(staleRunning, stalePending, succeeded);
        db.MediaAssets.Add(new MediaAsset { PipelineRunId = run.Id, AssetType = "video", FileName = "video.mp4", LocalPath = CreateTempFile("video"), SizeBytes = 5 });
        db.PublishedVideos.Add(new PublishedVideo { PipelineRunId = run.Id, Title = "Sky", Status = "UploadFailed" });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var summary = await service.RecoverStaleJobsAsync(new RecoverStaleJobsRequest("manual", "recover", ThresholdMinutes: 60), CancellationToken.None);

        Assert.Equal(2, summary.MarkedStaleJobs);
        Assert.Equal(2, summary.RequeuedJobs);
        Assert.Contains(staleRunning.Id, summary.AffectedJobIds);
        Assert.DoesNotContain(succeeded.Id, summary.AffectedJobIds);
        Assert.Equal(PipelineJobStatus.Pending, await db.PipelineJobs.Where(x => x.Id == staleRunning.Id).Select(x => x.Status).SingleAsync());
        Assert.True(await db.RecoveryOperations.AnyAsync(x => x.OperationType == RecoveryOperationType.RecoverStaleJobs && x.Status == RecoveryOperationStatus.Completed));
    }

    [Fact]
    public async Task RetryPublish_UsesStoredAssets_AndIsIdempotentForThumbnailRecovery()
    {
        await using var db = CreateDb();
        var run = new PipelineRun { RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.SpaceNews, LocationName = "Pune", Status = PipelineRunStatus.Succeeded, PublishToYouTube = true };
        var videoPath = CreateTempFile("video");
        var thumbPath = CreateTempFile("thumb");
        db.PipelineRuns.Add(run);
        db.GeneratedScripts.Add(new GeneratedScript { PipelineRunId = run.Id, ContentType = run.ContentType, ScriptDate = run.RunDate, Prompt = "p", ScriptBody = "body", Title = "Title", Description = "Desc", TagsCsv = "astronomy", OptimizedTitle = "Optimized", OptimizedDescription = "Better Desc", OptimizedTagsCsv = "space,news", EstimatedDurationSeconds = 42 });
        db.MediaAssets.AddRange(
            new MediaAsset { PipelineRunId = run.Id, AssetType = "video", FileName = Path.GetFileName(videoPath), LocalPath = videoPath, SizeBytes = 5 },
            new MediaAsset { PipelineRunId = run.Id, AssetType = "thumbnail", FileName = Path.GetFileName(thumbPath), LocalPath = thumbPath, SizeBytes = 5 });
        db.PublishedVideos.Add(new PublishedVideo { PipelineRunId = run.Id, Title = "Optimized", Status = "UploadFailed" });
        await db.SaveChangesAsync();

        var youtube = new TrackingYouTubePublisher();
        var service = CreateService(db, youtubePublisher: youtube, youTubeService: youtube);

        var result = await service.RetryPublishAsync(run.Id, new RetryPublishRequest("manual", "retry publish", PublishToYouTube: true), CancellationToken.None);
        Assert.Equal(1, youtube.UploadCalls);
        Assert.Equal("Published", await db.PublishedVideos.Where(x => x.PipelineRunId == run.Id).Select(x => x.Status).SingleAsync());

        var thumbnailOnly = await service.RetryPublishAsync(run.Id, new RetryPublishRequest("manual", "thumb", RetryThumbnailOnly: true, PublishToYouTube: true), CancellationToken.None);
        Assert.Equal(1, youtube.UploadCalls);
        Assert.Equal(2, youtube.ThumbnailCalls);
        Assert.NotEqual(result.RecoveryOperationId, thumbnailOnly.RecoveryOperationId);
    }

    [Fact]
    public async Task RetryArchive_UpdatesStoredBlobUrls()
    {
        await using var db = CreateDb();
        var run = new PipelineRun { RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.DailySkyGuide, LocationName = "Pune", Status = PipelineRunStatus.Succeeded };
        var videoPath = CreateTempFile("video");
        var audioPath = CreateTempFile("audio");
        db.PipelineRuns.Add(run);
        db.MediaAssets.AddRange(
            new MediaAsset { PipelineRunId = run.Id, AssetType = "video", FileName = "video.mp4", LocalPath = videoPath, SizeBytes = 5 },
            new MediaAsset { PipelineRunId = run.Id, AssetType = "audio", FileName = "audio.mp3", LocalPath = audioPath, SizeBytes = 5 });
        db.PublishedVideos.Add(new PublishedVideo { PipelineRunId = run.Id, Title = "Sky", Status = "UploadFailed" });
        await db.SaveChangesAsync();

        var service = CreateService(db, blobService: new FakeBlobService());
        await service.RetryArchiveAsync(run.Id, new RetryArchiveRequest("manual", "archive"), CancellationToken.None);

        Assert.Equal("https://blob/video.mp4", await db.MediaAssets.Where(x => x.PipelineRunId == run.Id && x.AssetType == "video").Select(x => x.PublicUrl).SingleAsync());
        Assert.Equal("https://blob/video.mp4", await db.PublishedVideos.Where(x => x.PipelineRunId == run.Id).Select(x => x.BlobUrl).SingleAsync());
    }

    [Fact]
    public async Task Cleanup_RemovesOnlyExpiredRecords_AndWorkingFiles()
    {
        await using var db = CreateDb();
        var oldStage = new PipelineStageExecution { PipelineRunId = Guid.NewGuid(), StageName = "Render", StartedAt = DateTimeOffset.UtcNow.AddDays(-45) };
        var newStage = new PipelineStageExecution { PipelineRunId = Guid.NewGuid(), StageName = "Render", StartedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var oldJob = new PipelineJob { JobType = PipelineJobType.ArchiveAssets, RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.SpaceNews, LocationName = "Pune", Status = PipelineJobStatus.Failed, ScheduledAt = DateTimeOffset.UtcNow.AddDays(-45) };
        var runningJob = new PipelineJob { JobType = PipelineJobType.PublishVideo, RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.SpaceNews, LocationName = "Pune", Status = PipelineJobStatus.Running, ScheduledAt = DateTimeOffset.UtcNow.AddDays(-45) };
        var oldAnalytics = new VideoAnalytics { VideoId = "v1", RetrievedAt = DateTimeOffset.UtcNow.AddDays(-120), ContentType = ContentType.SpaceNews };
        db.AddRange(oldStage, newStage, oldJob, runningJob, oldAnalytics);
        await db.SaveChangesAsync();

        var workingDir = Directory.CreateTempSubdirectory("maintenance").FullName;
        var oldFile = Path.Combine(workingDir, "old.json");
        var freshFile = Path.Combine(workingDir, "fresh.json");
        await File.WriteAllTextAsync(oldFile, "old");
        await File.WriteAllTextAsync(freshFile, "fresh");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-30));

        var service = new MaintenanceService(db, Options.Create(new MaintenanceOptions
        {
            WorkingDirectory = workingDir,
            WorkingFileRetentionDays = 14,
            JobRetentionDays = 30,
            StageRetentionDays = 30,
            AnalyticsRetentionDays = 90,
            StaleJobThresholdMinutes = 60
        }), NullLogger<MaintenanceService>.Instance);

        var summary = await service.CleanupAsync(new CleanupMaintenanceRequest("manual", "cleanup", DeleteWorkingFiles: true, DeleteDbRecords: true, DeleteAnalytics: true), CancellationToken.None);

        Assert.Equal(1, summary.DeletedStageRecords);
        Assert.Equal(1, summary.DeletedJobRecords);
        Assert.Equal(1, summary.DeletedAnalyticsRecords);
        Assert.Equal(1, summary.DeletedWorkingFiles);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(freshFile));
        Assert.True(await db.PipelineJobs.AnyAsync(x => x.Id == runningJob.Id));
    }

    private static MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private static RunOperationsService CreateService(
        MediaFactoryDbContext db,
        IAzureBlobStorageService? blobService = null,
        TrackingYouTubePublisher? youTubeService = null,
        IYouTubeThumbnailPublisher? youtubePublisher = null)
    {
        return new RunOperationsService(
            db,
            new FakeQueue(),
            blobService ?? new FakeBlobService(),
            youTubeService ?? new TrackingYouTubePublisher(),
            new FakeContextProvider(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeShortsVideoRenderService(),
            NullLogger<RunOperationsService>.Instance,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = Path.Combine(Path.GetTempPath(), "media-output"), WorkingFileRetentionDays = 14, JobRetentionDays = 30, StageRetentionDays = 30, AnalyticsRetentionDays = 90, StaleJobThresholdMinutes = 60 }),
            youtubePublisher ?? youTubeService);
    }

    private static string CreateTempFile(string contents)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed class FakeQueue : IPipelineJobQueue
    {
        public List<EnqueuePipelineJobRequest> Requests { get; } = [];
        public Task<PipelineJob> EnqueueAsync(EnqueuePipelineJobRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new PipelineJob
            {
                JobType = request.JobType,
                ParentPipelineRunId = request.ParentPipelineRunId,
                RunDate = request.RunDate,
                ContentType = request.ContentType,
                LocationName = request.LocationName,
                TimeZone = request.TimeZone,
                PublishToYouTube = request.PublishToYouTube
            });
        }
    }

    private sealed class FakeBlobService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new BlobUploadResult { VideoUrl = "https://blob/video.mp4", AudioUrl = "https://blob/audio.mp3", ThumbnailUrl = "https://blob/thumb.png" });
    }

    private sealed class TrackingYouTubePublisher : IYouTubePublishingService, IYouTubeThumbnailPublisher
    {
        public int UploadCalls { get; private set; }
        public int ThumbnailCalls { get; private set; }

        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
        {
            UploadCalls += 1;
            return Task.FromResult<string?>("video-123");
        }

        public Task<bool> UploadThumbnailAsync(string videoId, string thumbnailPath, CancellationToken cancellationToken)
        {
            ThumbnailCalls += 1;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeContextProvider : IAstronomyContextProvider
    {
        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
            => Task.FromResult(new AstronomyContext { Date = date, LocationName = locationName, TimeZone = timeZone });
    }

    private sealed class FakeShortsVideoRenderService : IShortsVideoRenderService
    {
        public Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var video = Path.Combine(outputDirectory, "short.mp4");
            var audio = Path.Combine(outputDirectory, "short.mp3");
            File.WriteAllText(video, "video");
            File.WriteAllText(audio, "audio");
            return Task.FromResult(new ShortVideoRenderResult
            {
                Script = new ShortScriptResult { Hook = "Hook", ShortScript = "Short", Title = "Short", EstimatedDurationSeconds = 30 },
                VideoPath = video,
                AudioPath = audio,
                PublishStatus = publishToYouTube ? "Published" : "Draft"
            });
        }
    }
}
