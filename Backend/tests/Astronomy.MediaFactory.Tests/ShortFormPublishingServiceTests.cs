using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ShortFormPublishingServiceTests
{
    [Fact]
    public async Task PublishAsync_FansOutAcrossEnabledPlatforms()
    {
        var service = CreateService(
            new TestPublisher(ShortFormPlatform.YouTubeShorts, PlatformPublicationStatus.Published),
            new TestPublisher(ShortFormPlatform.InstagramReels, PlatformPublicationStatus.Published),
            new TestPublisher(ShortFormPlatform.Facebook, PlatformPublicationStatus.Published),
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = true, FacebookEnabled = true },
            new FakePipelineRepository());

        var results = await service.PublishAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.All(results, x => Assert.Equal(PlatformPublicationStatus.Published, x.Status));
    }

    [Fact]
    public async Task PublishAsync_RetriesTransientPublisherFailures()
    {
        var flakyPublisher = new FlakyPublisher(ShortFormPlatform.YouTubeShorts, failuresBeforeSuccess: 2);
        var service = CreateService(
            flakyPublisher,
            new TestPublisher(ShortFormPlatform.InstagramReels, PlatformPublicationStatus.Skipped),
            new TestPublisher(ShortFormPlatform.Facebook, PlatformPublicationStatus.Skipped),
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = false, FacebookEnabled = false, PublishRetryAttempts = 3, RetryBaseDelaySeconds = 1, MaxRetryDelaySeconds = 2 },
            new FakePipelineRepository());

        var result = await service.PublishAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(3, flakyPublisher.AttemptCount);
        Assert.Equal(PlatformPublicationStatus.Published, result.Single(x => x.Platform == ShortFormPlatform.YouTubeShorts).Status);
    }

    [Fact]
    public async Task PublishAsync_IsolatesPlatformFailures()
    {
        var service = CreateService(
            new TestPublisher(ShortFormPlatform.YouTubeShorts, PlatformPublicationStatus.Published),
            new ThrowingPublisher(ShortFormPlatform.InstagramReels),
            new TestPublisher(ShortFormPlatform.Facebook, PlatformPublicationStatus.Published),
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = true, FacebookEnabled = true },
            new FakePipelineRepository());

        var results = await service.PublishAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(PlatformPublicationStatus.Published, results.Single(x => x.Platform == ShortFormPlatform.YouTubeShorts).Status);
        Assert.Equal(PlatformPublicationStatus.Failed, results.Single(x => x.Platform == ShortFormPlatform.InstagramReels).Status);
        Assert.Equal(PlatformPublicationStatus.Published, results.Single(x => x.Platform == ShortFormPlatform.Facebook).Status);
    }

    [Fact]
    public async Task PublishAsync_ReturnsSkippedWhenPlatformDisabled()
    {
        var service = CreateService(
            new TestPublisher(ShortFormPlatform.YouTubeShorts, PlatformPublicationStatus.Published),
            new TestPublisher(ShortFormPlatform.InstagramReels, PlatformPublicationStatus.Published),
            new TestPublisher(ShortFormPlatform.Facebook, PlatformPublicationStatus.Published),
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = false, FacebookEnabled = false },
            new FakePipelineRepository());

        var results = await service.PublishAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(PlatformPublicationStatus.Published, results.Single(x => x.Platform == ShortFormPlatform.YouTubeShorts).Status);
        Assert.Equal(PlatformPublicationStatus.Skipped, results.Single(x => x.Platform == ShortFormPlatform.InstagramReels).Status);
        Assert.Equal(PlatformPublicationStatus.Skipped, results.Single(x => x.Platform == ShortFormPlatform.Facebook).Status);
    }

    [Fact]
    public async Task PublishAsync_SkipsDuplicatePublishedRecords()
    {
        var request = BuildRequest();
        var repo = new FakePipelineRepository();
        repo.Records.Add(new PlatformPublicationRecord
        {
            ParentShortVideoId = request.ParentShortVideoId,
            Platform = ShortFormPlatform.YouTubeShorts,
            Status = PlatformPublicationStatus.Published,
            ExternalPostId = "yt-123",
            ExternalUrl = "https://youtube.com/shorts/yt-123",
            PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        var publisher = new TestPublisher(ShortFormPlatform.YouTubeShorts, PlatformPublicationStatus.Published);
        var service = CreateService(
            publisher,
            new TestPublisher(ShortFormPlatform.InstagramReels, PlatformPublicationStatus.Skipped),
            new TestPublisher(ShortFormPlatform.Facebook, PlatformPublicationStatus.Skipped),
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = false, FacebookEnabled = false },
            repo);

        var results = await service.PublishAsync(request, CancellationToken.None);

        var youtube = results.Single(x => x.Platform == ShortFormPlatform.YouTubeShorts);
        Assert.Equal(PlatformPublicationStatus.Skipped, youtube.Status);
        Assert.Equal("yt-123", youtube.ExternalPostId);
        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public async Task PublishAsync_SkipsRapidRetryAttemptsDuringCooldown()
    {
        var request = BuildRequest();
        var repo = new FakePipelineRepository();
        repo.Records.Add(new PlatformPublicationRecord
        {
            ParentShortVideoId = request.ParentShortVideoId,
            Platform = ShortFormPlatform.YouTubeShorts,
            Status = PlatformPublicationStatus.Failed,
            ErrorMessage = "rate limited"
        });

        var publisher = new TestPublisher(ShortFormPlatform.YouTubeShorts, PlatformPublicationStatus.Published);
        var service = CreateService(
            publisher,
            new TestPublisher(ShortFormPlatform.InstagramReels, PlatformPublicationStatus.Skipped),
            new TestPublisher(ShortFormPlatform.Facebook, PlatformPublicationStatus.Skipped),
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = false, FacebookEnabled = false, PublishRetryCooldownSeconds = 60 },
            repo);

        var results = await service.PublishAsync(request, CancellationToken.None);

        Assert.Equal(PlatformPublicationStatus.Skipped, results.Single(x => x.Platform == ShortFormPlatform.YouTubeShorts).Status);
        Assert.Equal(0, publisher.CallCount);
    }



    [Fact]
    public async Task FacebookPublisher_MapsProcessingTimeoutToPublishedWarningResult()
    {
        var publisher = new FacebookPlatformPublisher(
            new FakeFacebookReelPublishService(new MetaPublishResult
            {
                Success = true,
                Platform = "Facebook",
                VideoId = "video-123",
                Url = "/reel/video-123",
                PublishedVerified = false,
                Warning = "Facebook Reel uploaded but public visibility could not be verified before processing timeout."
            }),
            Options.Create(new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true }),
            NullLogger<FacebookPlatformPublisher>.Instance);

        var result = await publisher.PublishAsync(new PlatformPublicationTarget
        {
            Platform = ShortFormPlatform.Facebook,
            Enabled = true,
            Title = "Facebook Reel",
            Caption = "Caption",
            VideoPath = "short-video.mp4"
        }, CancellationToken.None);

        Assert.Equal(PlatformPublicationStatus.Published, result.Status);
        Assert.Equal("Facebook", result.Platform.ToString());
        Assert.Equal("video-123", result.ExternalPostId);
        Assert.Equal("/reel/video-123", result.ExternalUrl);
        Assert.False(result.PublishedVerified);
        Assert.Equal("Processing not verified before timeout.", result.Warning);
    }

    [Fact]
    public async Task ExternalCancellation_StillCancelsPublishing()
    {
        var repository = new FakePipelineRepository();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService(
            new TestPublisher(ShortFormPlatform.YouTubeShorts, PlatformPublicationStatus.Skipped),
            new TestPublisher(ShortFormPlatform.InstagramReels, PlatformPublicationStatus.Skipped),
            new CancelingPublisher(ShortFormPlatform.Facebook),
            new PlatformPublishingOptions
            {
                FacebookEnabled = true,
                InstagramReelsEnabled = false,
                YouTubeShortsEnabled = false
            },
            repository);

        var request = BuildRequest();
        request = new ShortFormPublicationRequest
        {
            ParentShortVideoId = request.ParentShortVideoId,
            ContentType = request.ContentType,
            PublishToYouTube = false,
            Title = request.Title,
            Caption = request.Caption,
            HookLine = request.HookLine,
            Tags = request.Tags,
            Hashtags = request.Hashtags,
            VideoPath = request.VideoPath,
            ThumbnailPath = request.ThumbnailPath,
            Language = request.Language
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.PublishAsync(request, cts.Token));
    }

    private static ShortFormPublishingService CreateService(
        IShortFormPlatformPublisher youtube,
        IShortFormPlatformPublisher instagram,
        IShortFormPlatformPublisher facebook,
        PlatformPublishingOptions options,
        IPipelineRepository repository)
        => new(
            [youtube, instagram, facebook],
            new PlatformMetadataFormatter(options),
            repository,
            Options.Create(options),
            NullLogger<ShortFormPublishingService>.Instance);

    private static ShortFormPublicationRequest BuildRequest()
        => new()
        {
            ParentShortVideoId = Guid.NewGuid(),
            ContentType = ContentType.SpaceNews,
            PublishToYouTube = true,
            Title = "Meteor shower in 30 seconds",
            Caption = "Fast meteor shower breakdown.",
            HookLine = "Don't miss tonight's meteor streaks.",
            Tags = ["astronomy", "meteor"],
            Hashtags = ["#astronomy", "#meteor"],
            VideoPath = "short.mp4",
            ThumbnailPath = "thumb.png"
        };

    private sealed class FakePipelineRepository : IPipelineRepository
    {
        public List<PlatformPublicationRecord> Records { get; } = [];

        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPlatformPublicationRecordAsync(PlatformPublicationRecord record, CancellationToken cancellationToken) { Records.Add(record); return Task.CompletedTask; }
        public Task AddMonetizationRecordAsync(MonetizationRecord monetizationRecord, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineJob>>([]);
        public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<PlatformPublicationRecord?> GetPlatformPublicationRecordAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PlatformPublicationRecord?>(Records.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyCollection<PlatformPublicationRecord>> GetRecentPlatformPublicationRecordsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformPublicationRecord>>(Records.Take(take).ToArray());
        public Task<IReadOnlyCollection<PlatformPublicationRecord>> GetPlatformPublicationRecordsByShortIdAsync(Guid shortVideoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformPublicationRecord>>(Records.Where(x => x.ParentShortVideoId == shortVideoId).ToArray());
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestPublisher : IShortFormPlatformPublisher
    {
        private readonly PlatformPublicationStatus _status;

        public TestPublisher(ShortFormPlatform platform, PlatformPublicationStatus status)
        {
            Platform = platform;
            _status = status;
        }

        public int CallCount { get; private set; }
        public ShortFormPlatform Platform { get; }

        public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
        {
            CallCount += 1;
            target.Status = _status;
            target.ExternalPostId = $"{Platform}-123";
            return Task.FromResult(target);
        }
    }



    private sealed class FakeFacebookReelPublishService : IFacebookReelPublishService
    {
        private readonly MetaPublishResult _result;

        public FakeFacebookReelPublishService(MetaPublishResult result) => _result = result;

        public Task<MetaPublishResult> PublishReelAsync(MetaPublishRequest request, CancellationToken cancellationToken)
            => Task.FromResult(_result);
    }

    private sealed class CancelingPublisher : IShortFormPlatformPublisher
    {
        public CancelingPublisher(ShortFormPlatform platform) => Platform = platform;

        public ShortFormPlatform Platform { get; }

        public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
            => throw new OperationCanceledException(cancellationToken);
    }

    private sealed class ThrowingPublisher : IShortFormPlatformPublisher
    {
        public ThrowingPublisher(ShortFormPlatform platform) => Platform = platform;

        public ShortFormPlatform Platform { get; }

        public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
            => throw new InvalidOperationException("publisher failed");
    }

    private sealed class FlakyPublisher : IShortFormPlatformPublisher
    {
        private readonly int _failuresBeforeSuccess;

        public FlakyPublisher(ShortFormPlatform platform, int failuresBeforeSuccess)
        {
            Platform = platform;
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int AttemptCount { get; private set; }
        public ShortFormPlatform Platform { get; }

        public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
        {
            AttemptCount += 1;
            if (AttemptCount <= _failuresBeforeSuccess)
            {
                throw new IOException("transient network fault");
            }

            target.Status = PlatformPublicationStatus.Published;
            target.ExternalPostId = $"{Platform}-123";
            return Task.FromResult(target);
        }
    }
}
