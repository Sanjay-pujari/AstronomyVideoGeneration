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
            catch (Exception ex)
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
    private readonly InstagramPublishingOptions _options;
    private readonly ILogger<InstagramReelsPlatformPublisher> _logger;

    public InstagramReelsPlatformPublisher(IOptions<InstagramPublishingOptions> options, ILogger<InstagramReelsPlatformPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ShortFormPlatform Platform => ShortFormPlatform.InstagramReels;

    public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
    {
        if (!_options.PublishingEnabled || string.IsNullOrWhiteSpace(_options.AccessToken) || string.IsNullOrWhiteSpace(_options.BusinessAccountId))
        {
            _logger.LogWarning("Instagram Reels publishing skipped because the business account id or access token is missing.");
            target.Status = PlatformPublicationStatus.Skipped;
            target.ErrorMessage = "Instagram Reels publishing is not configured. Wire the Meta Graph API here.";
            return Task.FromResult(target);
        }

        _logger.LogWarning("Instagram Reels publishing is configured but the live Graph API upload workflow has not been wired yet.");
        target.Status = PlatformPublicationStatus.Failed;
        target.ErrorMessage = "Instagram Reels publishing integration point is ready, but the live Graph API upload call still needs to be implemented.";
        return Task.FromResult(target);
    }
}

public sealed class FacebookPlatformPublisher : IShortFormPlatformPublisher
{
    private readonly FacebookPublishingOptions _options;
    private readonly ILogger<FacebookPlatformPublisher> _logger;

    public FacebookPlatformPublisher(IOptions<FacebookPublishingOptions> options, ILogger<FacebookPlatformPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ShortFormPlatform Platform => ShortFormPlatform.Facebook;

    public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
    {
        if (!_options.PublishingEnabled || string.IsNullOrWhiteSpace(_options.AccessToken) || string.IsNullOrWhiteSpace(_options.PageId))
        {
            _logger.LogWarning("Facebook publishing skipped because the page id or access token is missing.");
            target.Status = PlatformPublicationStatus.Skipped;
            target.ErrorMessage = "Facebook publishing is not configured. Wire the Meta Graph API here.";
            return Task.FromResult(target);
        }

        _logger.LogWarning("Facebook short-form publishing is configured but the live Graph API upload workflow has not been wired yet.");
        target.Status = PlatformPublicationStatus.Failed;
        target.ErrorMessage = "Facebook publishing integration point is ready, but the live Graph API upload call still needs to be implemented.";
        return Task.FromResult(target);
    }
}
