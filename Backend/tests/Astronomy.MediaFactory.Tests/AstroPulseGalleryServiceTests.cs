using Astronomy.MediaFactory.Contracts;
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
}
