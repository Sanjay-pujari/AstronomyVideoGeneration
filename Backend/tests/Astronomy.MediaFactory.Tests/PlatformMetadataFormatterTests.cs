using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PlatformMetadataFormatterTests
{
    private readonly PlatformMetadataFormatter _formatter = new(new PlatformPublishingOptions
    {
        YouTubeShortsPreferredPublishLocalTime = "19:45",
        InstagramReelsPreferredPublishLocalTime = "21:15",
        FacebookPreferredPublishLocalTime = "20:15"
    });

    [Fact]
    public void FormatTarget_ForYouTubeShorts_UsesHookAsTitleLikeCaptionAndMinimalCta()
    {
        var result = _formatter.FormatTarget(ShortFormPlatform.YouTubeShorts, BuildRequest());

        var lines = result.Caption.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("Watch Jupiter rise tonight before the clouds roll in.", lines[0]);
        Assert.Contains("#shorts", result.Hashtags, StringComparer.OrdinalIgnoreCase);
        Assert.InRange(result.Hashtags.Count, 3, 5);
        Assert.Contains("Look up tonight.", result.Caption);
        Assert.True(result.Caption.Length <= 120);
        Assert.Equal("19:45", result.PreferredPublishLocalTime);
    }

    [Fact]
    public void FormatTarget_ForInstagramReels_PlacesHookFirstAndCapsHashtagCount()
    {
        var result = _formatter.FormatTarget(ShortFormPlatform.InstagramReels, BuildRequest());

        var paragraphs = result.Caption.Split("\n\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Watch Jupiter rise tonight before the clouds roll in.", paragraphs[0]);
        Assert.Contains("Save this for tonight.", result.Caption);
        Assert.Contains("#astronomy", result.Hashtags, StringComparer.OrdinalIgnoreCase);
        Assert.InRange(result.Hashtags.Count, 5, 10);
        Assert.DoesNotContain(result.Hashtags, x => x.Equals("#shorts", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.Caption.Length <= 280);
        Assert.Equal("21:15", result.PreferredPublishLocalTime);
    }

    [Fact]
    public void FormatTarget_ForFacebook_PlacesHookFirstAndKeepsHashtagsOptionalAndTight()
    {
        var result = _formatter.FormatTarget(ShortFormPlatform.Facebook, BuildRequest());

        var paragraphs = result.Caption.Split("\n\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Watch Jupiter rise tonight before the clouds roll in.", paragraphs[0]);
        Assert.Contains("Follow for more night-sky updates", result.Caption);
        Assert.InRange(result.Hashtags.Count, 0, 3);
        Assert.True(result.Caption.Length <= 360);
        Assert.Equal("20:15", result.PreferredPublishLocalTime);
    }


    [Fact]
    public void FormatTarget_ForFacebook_AllowsZeroHashtagsWhenNoneProvided()
    {
        var request = Clone(BuildRequest(), tags: Array.Empty<string>(), hashtags: Array.Empty<string>());

        var result = _formatter.FormatTarget(ShortFormPlatform.Facebook, request);

        Assert.Empty(result.Hashtags);
        Assert.DoesNotContain('#', result.Caption);
    }

    [Fact]
    public void FormatTarget_NormalizesWhitespaceAndDeduplicatesHashtags()
    {
        var result = _formatter.FormatTarget(ShortFormPlatform.InstagramReels, BuildNoisyRequest());

        Assert.DoesNotContain("  ", result.Caption);
        Assert.Equal(result.Hashtags.Count, result.Hashtags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(result.Hashtags, tag => Assert.StartsWith("#", tag));
    }

    [Fact]
    public void FormatTarget_UsesTitleAsFallbackWhenHookMissing()
    {
        var request = Clone(BuildRequest(), hookLine: "   ");

        var result = _formatter.FormatTarget(ShortFormPlatform.YouTubeShorts, request);
        var firstLine = result.Caption.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(result.Title, firstLine);
    }


    [Fact]
    public void FormatTarget_UsesSafeFallbacksForEmptyMetadata()
    {
        var request = Clone(BuildRequest(), title: "   ", caption: "   ", hookLine: "   ", tags: Array.Empty<string>(), hashtags: Array.Empty<string>());

        var result = _formatter.FormatTarget(ShortFormPlatform.YouTubeShorts, request);

        Assert.Equal("Astronomy update", result.Title);
        Assert.False(string.IsNullOrWhiteSpace(result.Caption));
        Assert.Contains("#shorts", result.Hashtags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatTarget_TrimsAtWordBoundaryWithoutAbruptCutoff()
    {
        var request = Clone(
            BuildRequest(),
            caption: "A fast sky update showing when to look up, where to aim, and why Jupiter is bright tonight with binocular-friendly context for backyard observers who want a clean and useful explanation before sunset arrives over the southern horizon.");

        var youtube = _formatter.FormatTarget(ShortFormPlatform.YouTubeShorts, request);
        var instagram = _formatter.FormatTarget(ShortFormPlatform.InstagramReels, request);

        Assert.True(youtube.Caption.Length <= 120);
        Assert.False(youtube.Caption.EndsWith("-", StringComparison.Ordinal));
        Assert.False(youtube.Caption.EndsWith(",", StringComparison.Ordinal));
        Assert.True(instagram.Caption.Length <= 280);
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
            Tags = ["astronomy", "jupiter", "night sky", "planet", "moon"],
            Hashtags = ["#astronomy", "#jupiter", "#nightsky", "#planet", "#moon", "#stargazing", "#space"],
            VideoPath = "short.mp4",
            ThumbnailPath = "thumb.png"
        };

    private static ShortFormPublicationRequest BuildNoisyRequest()
        => Clone(
            BuildRequest(),
            title: "  Watch   Jupiter rise tonight   over the southern horizon  ",
            caption: "  A fast   sky update   showing when to look up, where to aim, and why Jupiter is bright tonight.  ",
            hookLine: "  Watch   Jupiter rise tonight before the clouds roll in.  ",
            tags: ["astronomy", "jupiter", "night sky", "night sky"],
            hashtags: [" astronomy ", "#Jupiter", "#jupiter", " #night_sky "]);

    private static ShortFormPublicationRequest Clone(
        ShortFormPublicationRequest source,
        string? title = null,
        string? caption = null,
        string? hookLine = null,
        IReadOnlyCollection<string>? tags = null,
        IReadOnlyCollection<string>? hashtags = null)
        => new()
        {
            ParentShortVideoId = source.ParentShortVideoId,
            ContentType = source.ContentType,
            PublishToYouTube = source.PublishToYouTube,
            Title = title ?? source.Title,
            Caption = caption ?? source.Caption,
            HookLine = hookLine ?? source.HookLine,
            Tags = tags ?? source.Tags,
            Hashtags = hashtags ?? source.Hashtags,
            VideoPath = source.VideoPath,
            ThumbnailPath = source.ThumbnailPath
        };
}
