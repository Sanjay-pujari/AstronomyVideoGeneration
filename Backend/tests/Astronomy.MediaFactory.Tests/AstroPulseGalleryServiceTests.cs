using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstroPulseGalleryServiceTests
{
    [Fact]
    public async Task GenerateGalleryAsync_RequiresConfiguredAzureImage2()
    {
        var root = Path.Combine(Path.GetTempPath(), $"astropulse-gallery-{Guid.NewGuid():N}");
        var service = new AstroPulseGalleryService(Options.Create(new AzureOpenAIForImageOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateGalleryAsync(root, AstroPulseGalleryAspect.Landscape, CancellationToken.None));

        Assert.Contains("Phase 13 Gallery V3 requires Azure Image2 configuration", ex.Message);
    }

    [Fact]
    public void GalleryV3_ResultContract_IncludesValidationPath()
    {
        var result = new AstroPulseGalleryResult("gallery", ["gallery/gallery-01.png"], "gallery/gallery-review.json", "gallery/gallery-manifest.json", "gallery/gallery-generation-diagnostics.json", "gallery/phase-13-validation.json");

        Assert.Equal("gallery/phase-13-validation.json", result.ValidationPath);
    }
    [Theory]
    [MemberData(nameof(RequiredGalleryCoverage))]
    public void GalleryV3_Topics_PreserveEducationalSequence_ForRequiredEventLanguageAndAspectCoverage(string eventType, string language, AstroPulseGalleryAspect aspect)
    {
        var context = new AstroPulseGalleryService.GalleryContext(eventType, eventType, "story", "visual", "July 1, 2026", "9 PM", "US", language, EventObjectContextBuilder.FromJsonValues(eventType, eventType, [eventType], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);

        Assert.True(aspect.Width > 0);
        Assert.True(aspect.Height > 0);
        Assert.Equal(6, topics.Count);
        Assert.Equal(Enumerable.Range(1, 6), topics.Select(t => t.Number));
        Assert.All(topics, topic => Assert.Contains("Educational role", topic.AzureImage2Prompt));
        Assert.All(topics, topic => Assert.Contains("one educational idea per slide", topic.AzureImage2Prompt));
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("hi", true)]
    public void GalleryV3_Topics_LocalizeMetadataAndOverlayText(string language, bool expectHindi)
    {
        var context = new AstroPulseGalleryService.GalleryContext("Meteor Shower", "Meteor Shower", "story", "visual", "July 1, 2026", "9 PM", "US", language, EventObjectContextBuilder.FromJsonValues("Meteor Shower", "Meteor Shower", ["Perseids"], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);
        var text = string.Join(" ", topics.SelectMany(t => t.TextBlocks));

        Assert.Equal(expectHindi, text.Any(c => c >= '\u0900' && c <= '\u097F'));
    }

    [Theory]
    [MemberData(nameof(GalleryAspects))]
    public void GalleryV3_Aspects_CoverLandscapePortraitAndSquare(AstroPulseGalleryAspect aspect)
    {
        Assert.True(aspect.Width > 0);
        Assert.True(aspect.Height > 0);
    }

    public static IEnumerable<object[]> GalleryAspects()
    {
        yield return [AstroPulseGalleryAspect.Landscape];
        yield return [AstroPulseGalleryAspect.Portrait];
        yield return [AstroPulseGalleryAspect.Square];
    }

    public static IEnumerable<object[]> RequiredGalleryCoverage()
    {
        var events = new[] { "Solar Eclipse", "Lunar Eclipse", "Meteor Shower", "Planet Conjunction", "Planet Grouping" };
        var languages = new[] { "en", "hi" };
        var aspects = new[] { AstroPulseGalleryAspect.Landscape, AstroPulseGalleryAspect.Portrait, AstroPulseGalleryAspect.Square };
        foreach (var eventType in events)
        foreach (var language in languages)
        foreach (var aspect in aspects)
            yield return [eventType, language, aspect];
    }

}
