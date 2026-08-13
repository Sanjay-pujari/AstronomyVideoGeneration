using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2YouTubeLongCheckpointTests
{
    [Theory]
    [InlineData("English", "en", "English")]
    [InlineData("en", "en", "English")]
    [InlineData("Spanish", "es", "Spanish")]
    public void Caption_language_is_deterministically_mapped_from_plan(string input, string code, string name) =>
        Assert.Equal((code, name), Rc2PublishingExecutionService.ResolveCaptionLanguage(input));

    [Fact]
    public void Unknown_caption_language_fails_closed() =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ResolveCaptionLanguage("unknown"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_title_fails_closed(string title) =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateYouTubeMetadata(title, "private", "28"));

    [Fact]
    public void Provider_title_limit_is_enforced() =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateYouTubeMetadata(new string('a', 101), "private", "28"));

    [Theory]
    [InlineData("friends", "28")]
    [InlineData("private", "science")]
    public void Invalid_provider_metadata_fails_closed(string privacy, string category) =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateYouTubeMetadata("title", privacy, category));

    [Fact]
    public void Valid_provider_metadata_passes() =>
        Rc2PublishingExecutionService.ValidateYouTubeMetadata("title", "private", "28");
}
