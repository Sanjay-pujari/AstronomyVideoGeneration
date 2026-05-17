using System.Diagnostics;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class ShortFormPublishingService : IShortFormPublishingService
{
    private readonly PlatformPublishingOptions _options;
    private readonly IShortFormPlatformMetadataFormatter _formatter;
    private readonly IReadOnlyDictionary<ShortFormPlatform, IShortFormPlatformPublisher> _publishers;
    private readonly IPipelineRepository _repository;
    private readonly ILogger<ShortFormPublishingService> _logger;

    public ShortFormPublishingService(
        IEnumerable<IShortFormPlatformPublisher> publishers,
        IShortFormPlatformMetadataFormatter formatter,
        IPipelineRepository repository,
        IOptions<PlatformPublishingOptions> options,
        ILogger<ShortFormPublishingService> logger)
    {
        _formatter = formatter;
        _repository = repository;
        _options = options.Value;
        _logger = logger;
        _publishers = publishers.ToDictionary(x => x.Platform);
    }

    public async Task<IReadOnlyCollection<PlatformPublicationTarget>> PublishAsync(ShortFormPublicationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<PlatformPublicationTarget>();
        var correlationId = Activity.Current?.Id ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        var existingRecords = await _repository.GetPlatformPublicationRecordsByShortIdAsync(request.ParentShortVideoId, cancellationToken);

        foreach (var (platform, enabled, reason) in GetTargets(request))
        {
            var target = _formatter.FormatTarget(platform, request);
            target.Enabled = enabled;

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["ShortVideoId"] = request.ParentShortVideoId,
                ["Platform"] = platform.ToString()
            });

            if (!enabled)
            {
                target.Status = PlatformPublicationStatus.Skipped;
                target.ErrorMessage = reason;
                results.Add(target);
                continue;
            }

            var idempotentResult = TryBuildIdempotentResult(platform, request.ParentShortVideoId, target, existingRecords);
            if (idempotentResult is not null)
            {
                results.Add(idempotentResult);
                continue;
            }

            if (TryBuildCooldownResult(platform, request.ParentShortVideoId, target, existingRecords) is { } cooledDown)
            {
                results.Add(cooledDown);
                continue;
            }

            if (!_publishers.TryGetValue(platform, out var publisher))
            {
                target.Status = PlatformPublicationStatus.Failed;
                target.ErrorMessage = $"No platform publisher is registered for {platform}.";
                results.Add(target);
                continue;
            }

            _logger.LogInformation("Starting short-form publish for {Platform} and short video {ShortVideoId}. CorrelationId: {CorrelationId}", platform, request.ParentShortVideoId, correlationId);

            try
            {
                var result = await TransientRetryHelper.ExecuteAsync(
                    ct => publisher.PublishAsync(CloneTarget(target), ct),
                    _options.PublishRetryAttempts,
                    TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
                    TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds),
                    _logger,
                    "short-form publish",
                    platform.ToString(),
                    cancellationToken);

                _logger.LogInformation(
                    "Completed short-form publish for {Platform} and short video {ShortVideoId} with status {Status}. CorrelationId: {CorrelationId}",
                    platform,
                    request.ParentShortVideoId,
                    result.Status,
                    correlationId);

                results.Add(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Short-form publishing failed for {Platform} and short video {ShortVideoId}. CorrelationId: {CorrelationId}", platform, request.ParentShortVideoId, correlationId);
                target.Status = PlatformPublicationStatus.Failed;
                target.ErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? $"{platform} publish failed with an unknown error."
                    : ex.Message;
                results.Add(target);
            }
        }

        return results;
    }

    private PlatformPublicationTarget? TryBuildIdempotentResult(
        ShortFormPlatform platform,
        Guid shortVideoId,
        PlatformPublicationTarget target,
        IReadOnlyCollection<PlatformPublicationRecord> existingRecords)
    {
        var publishedRecord = existingRecords
            .Where(x => x.Platform == platform && x.Status == PlatformPublicationStatus.Published)
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedUtc)
            .FirstOrDefault();

        if (publishedRecord is null)
        {
            return null;
        }

        _logger.LogInformation("Skipping {Platform} publish for short video {ShortVideoId} because a published record already exists.", platform, shortVideoId);
        target.Status = PlatformPublicationStatus.Skipped;
        target.PublishedAt = publishedRecord.PublishedAt;
        target.ExternalPostId = publishedRecord.ExternalPostId;
        target.ExternalUrl = publishedRecord.ExternalUrl;
        target.ErrorMessage = $"Skipping duplicate publish because {platform} already succeeded for this short.";
        return target;
    }

    private PlatformPublicationTarget? TryBuildCooldownResult(
        ShortFormPlatform platform,
        Guid shortVideoId,
        PlatformPublicationTarget target,
        IReadOnlyCollection<PlatformPublicationRecord> existingRecords)
    {
        var latestAttempt = existingRecords
            .Where(x => x.Platform == platform)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefault();

        if (latestAttempt is null)
        {
            return null;
        }

        var cooldown = TimeSpan.FromSeconds(Math.Clamp(_options.PublishRetryCooldownSeconds, 1, 600));
        var retryAt = latestAttempt.CreatedUtc.Add(cooldown);
        if (retryAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        target.Status = PlatformPublicationStatus.Skipped;
        target.ErrorMessage = $"Skipping rapid retry for {platform}. Retry after {retryAt:O}.";
        _logger.LogWarning("Skipping {Platform} publish for short video {ShortVideoId} until {RetryAt} to avoid retry storms.", platform, shortVideoId, retryAt);
        return target;
    }

    private IEnumerable<(ShortFormPlatform Platform, bool Enabled, string Reason)> GetTargets(ShortFormPublicationRequest request)
    {
        yield return (ShortFormPlatform.YouTubeShorts, request.PublishToYouTube && _options.YouTubeShortsEnabled, request.PublishToYouTube ? "YouTube Shorts publishing is disabled by configuration." : "YouTube Shorts publishing was not requested for this run.");
        yield return (ShortFormPlatform.InstagramReels, _options.InstagramReelsEnabled, "Instagram Reels publishing is disabled by configuration.");
        yield return (ShortFormPlatform.Facebook, _options.FacebookEnabled, "Facebook publishing is disabled by configuration.");
    }

    private static PlatformPublicationTarget CloneTarget(PlatformPublicationTarget target)
        => new()
        {
            Platform = target.Platform,
            Enabled = target.Enabled,
            Title = target.Title,
            Caption = target.Caption,
            Hashtags = target.Hashtags.ToArray(),
            PreferredPublishLocalTime = target.PreferredPublishLocalTime,
            VideoPath = target.VideoPath,
            ThumbnailPath = target.ThumbnailPath,
            Status = target.Status,
            PublishedAt = target.PublishedAt,
            ExternalPostId = target.ExternalPostId,
            ExternalUrl = target.ExternalUrl,
            ErrorMessage = target.ErrorMessage,
            PublishedVerified = target.PublishedVerified,
            Warning = target.Warning,
            YouTubeShortEligible = target.YouTubeShortEligible
        };
}

public sealed class YouTubeShortsPlatformPublisher : IShortFormPlatformPublisher
{
    private readonly IYouTubePublishingService _youTubePublishingService;
    private readonly YouTubeOptions _options;
    private readonly RenderingOptions _renderingOptions;
    private readonly ILogger<YouTubeShortsPlatformPublisher> _logger;

    public YouTubeShortsPlatformPublisher(
        IYouTubePublishingService youTubePublishingService,
        IOptions<YouTubeOptions> options,
        IOptions<RenderingOptions>? renderingOptions = null,
        ILogger<YouTubeShortsPlatformPublisher>? logger = null)
    {
        _youTubePublishingService = youTubePublishingService;
        _options = options.Value;
        _renderingOptions = renderingOptions?.Value ?? new RenderingOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeShortsPlatformPublisher>.Instance;
    }

    public ShortFormPlatform Platform => ShortFormPlatform.YouTubeShorts;

    public async Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
    {
        if (!_options.PublishingEnabled)
        {
            target.Status = PlatformPublicationStatus.Skipped;
            target.ErrorMessage = "YouTube publishing credentials are not enabled.";
            return target;
        }

        target.Caption = YouTubeShortsValidation.EnsureShortsMarkerInDescription(target.Title, target.Caption);
        var validation = await YouTubeShortsValidation.ValidateBeforeUploadAsync(target.VideoPath, _renderingOptions.FfprobePath, cancellationToken);
        target.YouTubeShortEligible = validation.YouTubeShortEligible;
        if (!validation.YouTubeShortEligible)
        {
            _logger.LogWarning("Short video at {VideoPath} is not eligible for YouTube Shorts: {Warnings}", target.VideoPath, string.Join("; ", validation.Warnings));
        }

        var tagValues = target.Hashtags.Select(static x => x.TrimStart('#')).Where(static x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var videoId = await _youTubePublishingService.UploadAsync(target.VideoPath, target.Title, target.Caption, tagValues, _options.PrivacyStatus, cancellationToken);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            target.Status = PlatformPublicationStatus.Failed;
            target.ErrorMessage = "YouTube upload returned no video id.";
            return target;
        }

        target.Status = PlatformPublicationStatus.Published;
        target.PublishedAt = DateTimeOffset.UtcNow;
        target.ExternalPostId = videoId;
        target.ExternalUrl = $"https://www.youtube.com/shorts/{videoId}";
        return target;
    }
}

public sealed class InstagramReelsPlatformPublisher : IShortFormPlatformPublisher
{
    private readonly IInstagramReelPublishService _instagramReelPublishService;
    private readonly MetaPublishingOptions _options;
    private readonly ILogger<InstagramReelsPlatformPublisher> _logger;

    public InstagramReelsPlatformPublisher(
        IInstagramReelPublishService instagramReelPublishService,
        IOptions<MetaPublishingOptions> options,
        ILogger<InstagramReelsPlatformPublisher> logger)
    {
        _instagramReelPublishService = instagramReelPublishService;
        _options = options.Value;
        _logger = logger;
    }

    public ShortFormPlatform Platform => ShortFormPlatform.InstagramReels;

    public async Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
    {
        if (!MetaPublishingEnabled(_options) || !_options.PublishInstagramReel)
        {
            target.Status = PlatformPublicationStatus.Skipped;
            target.ErrorMessage = "Instagram Reels publishing is disabled by MetaPublishing configuration.";
            return target;
        }

        var result = await _instagramReelPublishService.PublishReelAsync(CreateMetaRequest(target, "Instagram", _options), cancellationToken);
        return ApplyMetaResult(target, result, _logger);
    }

    private static bool MetaPublishingEnabled(MetaPublishingOptions options)
        => options.Enabled && !string.Equals(options.Mode, "Disabled", StringComparison.OrdinalIgnoreCase);

    private static MetaPublishRequest CreateMetaRequest(PlatformPublicationTarget target, string platform, MetaPublishingOptions options)
    {
        var metaVideoPath = ResolveMetaVideoPath(target.VideoPath, options);
        var posterFrameApplied = !string.Equals(metaVideoPath, target.VideoPath, StringComparison.OrdinalIgnoreCase);
        return new MetaPublishRequest
        {
            Platform = platform,
            VideoPath = metaVideoPath,
            ShortThumbnailPath = target.ThumbnailPath ?? string.Empty,
            PlatformThumbnailPath = posterFrameApplied ? string.Empty : target.ThumbnailPath ?? string.Empty,
            ThumbnailSource = string.IsNullOrWhiteSpace(target.ThumbnailPath) ? ThumbnailSources.None : ThumbnailSources.GeneratedThumbnail,
            PosterFrameApplied = posterFrameApplied,
            PosterFrameImagePath = posterFrameApplied ? target.ThumbnailPath ?? string.Empty : string.Empty,
            PosterFrameDurationSeconds = posterFrameApplied ? Math.Clamp(options.PosterFrameDurationSeconds, 0.5d, 1.0d) : 0d,
            Caption = target.Caption,
            ShortTitle = target.Title,
            IsReel = true
        };
    }

    private static string ResolveMetaVideoPath(string videoPath, MetaPublishingOptions options)
    {
        if (!options.UsePosterFrameFallbackForReels || string.IsNullOrWhiteSpace(videoPath))
        {
            return videoPath;
        }

        var metaPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(videoPath)) ?? string.Empty, "short-video-meta.mp4");
        return File.Exists(metaPath) ? metaPath : videoPath;
    }

    private static PlatformPublicationTarget ApplyMetaResult(PlatformPublicationTarget target, MetaPublishResult result, ILogger logger)
    {
        target.Status = result.Success ? PlatformPublicationStatus.Published : PlatformPublicationStatus.Failed;
        target.PublishedAt = result.PublishedUtc;
        target.ExternalPostId = string.IsNullOrWhiteSpace(result.PostId) ? result.VideoId : result.PostId;
        target.ExternalUrl = result.Url;
        target.PublishedVerified = result.PublishedVerified;
        target.Warning = result.Warning;
        target.ErrorMessage = result.Error ?? result.Warning;

        if (!result.Success)
        {
            logger.LogWarning("{Platform} Reel publishing failed in MetaPublishing mode {Mode}: {Error}", result.Platform, result.Mode, result.Error);
        }

        return target;
    }
}

public sealed class FacebookPlatformPublisher : IShortFormPlatformPublisher
{
    private readonly IFacebookReelPublishService _facebookReelPublishService;
    private readonly MetaPublishingOptions _options;
    private readonly ILogger<FacebookPlatformPublisher> _logger;

    public FacebookPlatformPublisher(
        IFacebookReelPublishService facebookReelPublishService,
        IOptions<MetaPublishingOptions> options,
        ILogger<FacebookPlatformPublisher> logger)
    {
        _facebookReelPublishService = facebookReelPublishService;
        _options = options.Value;
        _logger = logger;
    }

    public ShortFormPlatform Platform => ShortFormPlatform.Facebook;

    public async Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
    {
        if (!MetaPublishingEnabled(_options) || !_options.PublishFacebookReel)
        {
            target.Status = PlatformPublicationStatus.Skipped;
            target.ErrorMessage = "Facebook publishing is disabled by MetaPublishing configuration.";
            return target;
        }

        var result = await _facebookReelPublishService.PublishReelAsync(CreateMetaRequest(target, "Facebook", _options), cancellationToken);
        return ApplyMetaResult(target, result, _logger);
    }

    private static bool MetaPublishingEnabled(MetaPublishingOptions options)
        => options.Enabled && !string.Equals(options.Mode, "Disabled", StringComparison.OrdinalIgnoreCase);

    private static MetaPublishRequest CreateMetaRequest(PlatformPublicationTarget target, string platform, MetaPublishingOptions options)
    {
        var metaVideoPath = ResolveMetaVideoPath(target.VideoPath, options);
        var posterFrameApplied = !string.Equals(metaVideoPath, target.VideoPath, StringComparison.OrdinalIgnoreCase);
        return new MetaPublishRequest
        {
            Platform = platform,
            VideoPath = metaVideoPath,
            ShortThumbnailPath = target.ThumbnailPath ?? string.Empty,
            PlatformThumbnailPath = posterFrameApplied ? string.Empty : target.ThumbnailPath ?? string.Empty,
            ThumbnailSource = string.IsNullOrWhiteSpace(target.ThumbnailPath) ? ThumbnailSources.None : ThumbnailSources.GeneratedThumbnail,
            PosterFrameApplied = posterFrameApplied,
            PosterFrameImagePath = posterFrameApplied ? target.ThumbnailPath ?? string.Empty : string.Empty,
            PosterFrameDurationSeconds = posterFrameApplied ? Math.Clamp(options.PosterFrameDurationSeconds, 0.5d, 1.0d) : 0d,
            Caption = target.Caption,
            ShortTitle = target.Title,
            IsReel = true
        };
    }

    private static string ResolveMetaVideoPath(string videoPath, MetaPublishingOptions options)
    {
        if (!options.UsePosterFrameFallbackForReels || string.IsNullOrWhiteSpace(videoPath))
        {
            return videoPath;
        }

        var metaPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(videoPath)) ?? string.Empty, "short-video-meta.mp4");
        return File.Exists(metaPath) ? metaPath : videoPath;
    }

    private static PlatformPublicationTarget ApplyMetaResult(PlatformPublicationTarget target, MetaPublishResult result, ILogger logger)
    {
        target.Status = result.Success ? PlatformPublicationStatus.Published : PlatformPublicationStatus.Failed;
        target.PublishedAt = result.PublishedUtc;
        target.ExternalPostId = string.IsNullOrWhiteSpace(result.PostId) ? result.VideoId : result.PostId;
        target.ExternalUrl = result.Url;
        target.PublishedVerified = result.PublishedVerified;
        target.Warning = result.Success && !result.PublishedVerified && !string.IsNullOrWhiteSpace(result.Warning)
            ? "Processing not verified before timeout."
            : result.Warning;
        target.ErrorMessage = result.Error ?? target.Warning;

        if (!result.Success)
        {
            logger.LogWarning("{Platform} Reel publishing failed in MetaPublishing mode {Mode}: {Error}", result.Platform, result.Mode, result.Error);
        }

        return target;
    }
}
