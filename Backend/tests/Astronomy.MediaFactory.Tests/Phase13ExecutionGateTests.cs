using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13ExecutionGateTests
{
    [Fact]
    public void Phase13NotApplicableWhenGalleryNotRequested() => Assert.False(Required("ShortVideo", "Thumbnail"));

    [Fact]
    public void Phase13ApplicableWhenGalleryRequested() => Assert.True(Required("Gallery"));

    [Fact]
    public void ThumbnailDoesNotImplicitlyRequestGallery() => Assert.False(Required("Thumbnail"));

    [Fact]
    public void HeroAssetDoesNotImplicitlyRequestGallery() => Assert.False(Required("HeroAsset"));

    [Fact]
    public void ShortVideoDoesNotImplicitlyRequestGallery() => Assert.False(Required("ShortVideo"));

    [Fact]
    public void LongVideoDoesNotImplicitlyRequestGallery() => Assert.False(Required("LongVideo"));

    [Fact]
    public void Phase13_ExecutesForExplicitGalleryRequest() =>
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset", "Gallery"], 13));

    private static bool Required(params string[] outputs) =>
        ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(outputs, 13);
}
