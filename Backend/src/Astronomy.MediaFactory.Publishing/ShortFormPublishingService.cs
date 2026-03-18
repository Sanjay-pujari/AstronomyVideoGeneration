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
    private readonly ILogger<ShortFormPublishingService> _logger;

    public ShortFormPublishingService(
        IEnumerable<IShortFormPlatformPublisher> publishers,
        IShortFormPlatformMetadataFormatter formatter,
        IOptions<PlatformPublishingOptions> options,
        ILogger<ShortFormPublishingService> logger)
    {
        _formatter = formatter;
        _options = options.Value;
        _logger = logger;
        _publishers = publishers.ToDictionary(x => x.Platform);
    }

    public async Task<IReadOnlyCollection<PlatformPublicationTarget>> PublishAsync(ShortFormPublicationRequest request, CancellationToken cancellationToken)
    {
        var results = new List<PlatformPublicationTarget>();
        foreach (var (platform, enabled, reason) in GetTargets(request))
        {
            var target = _formatter.FormatTarget(platform, request);
            target.Enabled = enabled;

            if (!enabled)
            {
                target.Status = PlatformPublicationStatus.Skipped;
                target.ErrorMessage = reason;
                results.Add(target);
                continue;
            }

            if (!_publishers.TryGetValue(platform, out var publisher))
            {
                target.Status = PlatformPublicationStatus.Failed;
                target.ErrorMessage = $"No platform publisher is registered for {platform}.";
                results.Add(target);
                continue;
            }

            try
            {
                results.Add(await publisher.PublishAsync(target, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Short-form publishing failed for {Platform} and short video {ShortVideoId}.", platform, request.ParentShortVideoId);
                target.Status = PlatformPublicationStatus.Failed;
                target.ErrorMessage = ex.Message;
                results.Add(target);
            }
        }

        return results;
    }

    private IEnumerable<(ShortFormPlatform Platform, bool Enabled, string Reason)> GetTargets(ShortFormPublicationRequest request)
    {
        yield return (ShortFormPlatform.YouTubeShorts, request.PublishToYouTube && _options.YouTubeShortsEnabled, request.PublishToYouTube ? "YouTube Shorts publishing is disabled by configuration." : "YouTube Shorts publishing was not requested for this run.");
        yield return (ShortFormPlatform.InstagramReels, _options.InstagramReelsEnabled, "Instagram Reels publishing is disabled by configuration.");
        yield return (ShortFormPlatform.Facebook, _options.FacebookEnabled, "Facebook publishing is disabled by configuration.");
    }
}

public sealed class YouTubeShortsPlatformPublisher : IShortFormPlatformPublisher
{
    private readonly IYouTubePublishingService _youTubePublishingService;
    private readonly YouTubeOptions _options;

    public YouTubeShortsPlatformPublisher(IYouTubePublishingService youTubePublishingService, IOptions<YouTubeOptions> options)
    {
        _youTubePublishingService = youTubePublishingService;
        _options = options.Value;
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
