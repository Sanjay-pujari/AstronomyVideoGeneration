using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2PublishingExecutionTests
{
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
