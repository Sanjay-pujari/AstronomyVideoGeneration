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
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = true, FacebookEnabled = true });

        var results = await service.PublishAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.All(results, x => Assert.Equal(PlatformPublicationStatus.Published, x.Status));
    }

    [Fact]
    public async Task PublishAsync_IsolatesPlatformFailures()
    {
        var service = CreateService(
            new TestPublisher(ShortFormPlatform.YouTubeShorts, PlatformPublicationStatus.Published),
            new ThrowingPublisher(ShortFormPlatform.InstagramReels),
            new TestPublisher(ShortFormPlatform.Facebook, PlatformPublicationStatus.Published),
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = true, FacebookEnabled = true });

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
            new PlatformPublishingOptions { YouTubeShortsEnabled = true, InstagramReelsEnabled = false, FacebookEnabled = false });

        var results = await service.PublishAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(PlatformPublicationStatus.Published, results.Single(x => x.Platform == ShortFormPlatform.YouTubeShorts).Status);
        Assert.Equal(PlatformPublicationStatus.Skipped, results.Single(x => x.Platform == ShortFormPlatform.InstagramReels).Status);
        Assert.Equal(PlatformPublicationStatus.Skipped, results.Single(x => x.Platform == ShortFormPlatform.Facebook).Status);
    }

    private static ShortFormPublishingService CreateService(
        IShortFormPlatformPublisher youtube,
        IShortFormPlatformPublisher instagram,
        IShortFormPlatformPublisher facebook,
        PlatformPublishingOptions options)
        => new(
            [youtube, instagram, facebook],
            new PlatformMetadataFormatter(),
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

    private sealed class TestPublisher : IShortFormPlatformPublisher
    {
        private readonly PlatformPublicationStatus _status;

        public TestPublisher(ShortFormPlatform platform, PlatformPublicationStatus status)
        {
            Platform = platform;
            _status = status;
        }

        public ShortFormPlatform Platform { get; }

        public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
        {
            target.Status = _status;
            target.ExternalPostId = $"{Platform}-123";
            return Task.FromResult(target);
        }
    }

    private sealed class ThrowingPublisher : IShortFormPlatformPublisher
    {
        public ThrowingPublisher(ShortFormPlatform platform) => Platform = platform;

        public ShortFormPlatform Platform { get; }

        public Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken)
            => throw new InvalidOperationException("publisher failed");
    }
}
