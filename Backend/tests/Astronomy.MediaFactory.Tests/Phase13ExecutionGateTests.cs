using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13ExecutionGateTests
{
    [Theory]
    [InlineData("ShortVideo")]
    [InlineData("LongVideo")]
    [InlineData("Thumbnail")]
    [InlineData("HeroAsset")]
    public void Phase13_DoesNotInferGalleryFromAnotherOutput(string requestedOutput) =>
        Assert.False(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs([requestedOutput], 13));

    [Fact]
    public void Phase13_ExecutesForExplicitGalleryRequest() =>
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset", "Gallery"], 13));
}
