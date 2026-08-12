using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2PublishingExecutionTests
{
    public static TheoryData<Rc2PublishingTarget, Action<PublishingTargetsOptions>> TargetMappings => new()
    {
        { Rc2PublishingTarget.YouTubeLong, options => options.YouTubeLong = true },
        { Rc2PublishingTarget.YouTubeShort, options => options.YouTubeShort = true },
        { Rc2PublishingTarget.FacebookLong, options => options.FacebookLong = true },
        { Rc2PublishingTarget.FacebookReel, options => options.FacebookReel = true },
        { Rc2PublishingTarget.InstagramReel, options => options.InstagramReel = true },
        { Rc2PublishingTarget.InstagramPost, options => options.InstagramPost = true },
        { Rc2PublishingTarget.InstagramCarousel, options => options.InstagramCarousel = true },
        { Rc2PublishingTarget.FacebookPost, options => options.FacebookPost = true },
        { Rc2PublishingTarget.FacebookCarousel, options => options.FacebookCarousel = true }
    };

    [Theory]
    [MemberData(nameof(TargetMappings))]
    public void Each_target_has_one_deterministic_options_mapping(
        Rc2PublishingTarget expected, Action<PublishingTargetsOptions> enable)
    {
        var targets = new PublishingTargetsOptions();
        enable(targets);

        Assert.All(Enum.GetValues<Rc2PublishingTarget>(), target => Assert.Equal(target == expected,
            Rc2PublishingExecutionService.IsTargetEnabled(target, targets)));
    }

    [Fact]
    public void Global_gate_blocks_all_targets()
    {
        var publishing = new PublishingOptions { Enabled = true };
        var youtube = new YouTubeOptions { PublishingEnabled = true };
        var targets = new PublishingTargetsOptions
        {
            YouTubeLong = true, YouTubeShort = true, FacebookLong = true, FacebookReel = true,
            InstagramReel = true, InstagramPost = true, InstagramCarousel = true,
            FacebookPost = true, FacebookCarousel = true
        };
        var meta = new MetaPublishingOptions
        {
            Enabled = true, PublishFacebookLong = true, PublishFacebookFullVideo = true,
            PublishFacebookReel = true, PublishInstagramReel = true
        };
        var platform = new PlatformPublishingOptions
        {
            YouTubeShortsEnabled = true, InstagramReelsEnabled = true, FacebookEnabled = true
        };

        Assert.All(Enum.GetValues<Rc2PublishingTarget>(), target => Assert.True(
            Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(target, publishing, youtube, targets, meta, platform), target.ToString()));

        publishing.Enabled = false;
        Assert.All(Enum.GetValues<Rc2PublishingTarget>(), target => Assert.False(
            Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(target, publishing, youtube, targets, meta, platform), target.ToString()));
    }

    [Fact]
    public void Provider_gates_are_independent_and_youtube_long_does_not_require_shorts_capability()
    {
        var publishing = new PublishingOptions { Enabled = true };
        var youtube = new YouTubeOptions { PublishingEnabled = true };
        var targets = new PublishingTargetsOptions { YouTubeLong = true, YouTubeShort = true, InstagramPost = true };
        var meta = new MetaPublishingOptions { Enabled = true, PublishInstagramReel = true };
        var platform = new PlatformPublishingOptions();

        Assert.True(Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(Rc2PublishingTarget.YouTubeLong,
            publishing, youtube, targets, meta, platform));
        Assert.False(Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(Rc2PublishingTarget.YouTubeShort,
            publishing, youtube, targets, meta, platform));
        youtube.PublishingEnabled = false;
        Assert.False(Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(Rc2PublishingTarget.YouTubeLong,
            publishing, youtube, targets, meta, platform));
        Assert.True(Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(Rc2PublishingTarget.InstagramPost,
            publishing, youtube, targets, meta, platform));
        meta.Enabled = false;
        Assert.False(Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(Rc2PublishingTarget.InstagramPost,
            publishing, youtube, targets, meta, platform));
    }

    [Fact]
    public void Explicit_target_enablement_has_no_scheduler_dependency()
    {
        Assert.True(Rc2PublishingExecutionService.IsTargetEffectivelyEnabled(Rc2PublishingTarget.YouTubeLong,
            new PublishingOptions { Enabled = true }, new YouTubeOptions { PublishingEnabled = true },
            new PublishingTargetsOptions { YouTubeLong = true }, new MetaPublishingOptions(),
            new PlatformPublishingOptions()));
    }

    [Fact]
    public void Video_roles_are_resolved_from_governed_authority()
    {
        var authority = Authority(
            Artifact("LongVideo", "long.mp4", 0), Artifact("ThumbnailLandscape", "landscape.jpg", 1),
            Artifact("LongCaptionSrt", "long.srt", 2), Artifact("ShortVideo", "short.mp4", 3),
            Artifact("ThumbnailPortrait", "portrait.jpg", 4), Artifact("ShortCaptionSrt", "short.srt", 5));

        Assert.Equal(new[] { "LongVideo", "ThumbnailLandscape", "LongCaptionSrt" },
            Rc2PublishingExecutionService.ResolveArtifacts(authority, Rc2PublishingTarget.YouTubeLong).Select(x => x.Role));
        Assert.Equal(new[] { "ShortVideo", "ThumbnailPortrait", "ShortCaptionSrt" },
            Rc2PublishingExecutionService.ResolveArtifacts(authority, Rc2PublishingTarget.YouTubeShort).Select(x => x.Role));
    }

    [Fact]
    public void Hero_fallback_and_gallery_order_are_governed()
    {
        var authority = Authority(Artifact("HeroSquare", "hero.jpg", 0),
            Artifact("GalleryImage", "second.jpg", 2), Artifact("GalleryImage", "first.jpg", 1));

        Assert.Equal("HeroSquare", Assert.Single(Rc2PublishingExecutionService.ResolveArtifacts(
            authority, Rc2PublishingTarget.InstagramPost)).Role);
        Assert.Equal(new[] { "first.jpg", "second.jpg" }, Rc2PublishingExecutionService.ResolveArtifacts(
            authority, Rc2PublishingTarget.InstagramCarousel).Select(x => x.Path));
    }

    [Fact]
    public void Missing_required_role_fails_closed()
    {
        var exception = Assert.Throws<Rc2PublishingControlException>(() =>
            Rc2PublishingExecutionService.ResolveArtifacts(Authority(Artifact("LongVideo", "long.mp4", 0)),
                Rc2PublishingTarget.YouTubeLong));
        Assert.Equal("RC2_PUBLISH_REQUIRED_ROLE_MISSING", exception.Code);
    }

    private static Phase20PublishingArtifact Artifact(string role, string path, int order) => new(role, path, 1, "aa", order);
    private static Phase20PublishingAuthoritySnapshot Authority(params Phase20PublishingArtifact[] artifacts) => new(
        "package", "checksum", "Succeeded", true, true, artifacts.Length,
        artifacts.GroupBy(x => x.Role).ToDictionary(x => x.Key, x => x.Count()), [], artifacts);
}
