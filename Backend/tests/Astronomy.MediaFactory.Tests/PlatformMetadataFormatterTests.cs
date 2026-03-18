using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PlatformMetadataFormatterTests
{
    private readonly PlatformMetadataFormatter _formatter = new();

    [Fact]
    public void FormatTarget_ForYouTubeShorts_AddsShortsHashtagAndCompactCaption()
    {
        var result = _formatter.FormatTarget(ShortFormPlatform.YouTubeShorts, BuildRequest());

        Assert.Contains("#shorts", result.Hashtags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Watch Jupiter", result.Caption, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Title.Length <= 90);
    }

    [Fact]
    public void FormatTarget_ForInstagramReels_UsesHookFirstCaptionAndTighterHashtags()
    {
        var result = _formatter.FormatTarget(ShortFormPlatform.InstagramReels, BuildRequest());

        Assert.StartsWith("Watch Jupiter rise tonight", result.Caption);
        Assert.Contains("Save this reel", result.Caption);
        Assert.True(result.Hashtags.Count <= 5);
    }

    [Fact]
    public void FormatTarget_ForFacebook_UsesDescriptiveCaptionAndLimitedHashtags()
    {
        var result = _formatter.FormatTarget(ShortFormPlatform.Facebook, BuildRequest());

        Assert.Contains("Follow for more astronomy shorts", result.Caption);
        Assert.True(result.Hashtags.Count <= 3);
    }

    private static ShortFormPublicationRequest BuildRequest()
        => new()
        {
            ParentShortVideoId = Guid.NewGuid(),
            ContentType = ContentType.DailySkyGuide,
            PublishToYouTube = true,
            Title = "Watch Jupiter rise tonight over the southern horizon",
            Caption = "A fast sky update showing when to look up, where to aim, and why Jupiter is bright tonight.",
            HookLine = "Watch Jupiter rise tonight before the clouds roll in.",
            Tags = ["astronomy", "jupiter", "night sky", "planet"],
            Hashtags = ["#astronomy", "#jupiter", "#nightsky", "#planets", "#stargazing", "#space"],
            VideoPath = "short.mp4",
            ThumbnailPath = "thumb.png"
        };
}
