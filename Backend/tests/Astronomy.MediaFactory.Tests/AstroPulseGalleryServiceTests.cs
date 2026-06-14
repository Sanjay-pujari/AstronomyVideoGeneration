using System.Text.Json;
using Astronomy.MediaFactory.Rendering;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstroPulseGalleryServiceTests
{
    [Fact]
    public async Task GenerateGeminidsGalleryAsync_WritesSixImagesAndValidationArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"astropulse-gallery-{Guid.NewGuid():N}");
        var service = new AstroPulseGalleryService();

        var result = await service.GenerateGeminidsGalleryAsync(root, new AstroPulseGalleryAspect("square", 1080, 1080), CancellationToken.None);

        Assert.Equal(6, result.ImagePaths.Count);
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.ReviewPath));
        Assert.True(File.Exists(result.DiagnosticsPath));

        foreach (var path in result.ImagePaths)
        {
            Assert.True(File.Exists(path));
            var info = Image.Identify(path);
            Assert.Equal(1080, info.Width);
            Assert.Equal(1080, info.Height);
        }

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath));
        Assert.Equal("Azure Image2 background + deterministic overlay", manifest.RootElement.GetProperty("architecture").GetString());
        Assert.Equal(6, manifest.RootElement.GetProperty("images").GetArrayLength());

        using var review = JsonDocument.Parse(await File.ReadAllTextAsync(result.ReviewPath));
        Assert.True(review.RootElement.GetProperty("accepted").GetBoolean());
        Assert.True(review.RootElement.GetProperty("noDuplicateConcepts").GetBoolean());
        Assert.True(review.RootElement.GetProperty("noDuplicateImageHashes").GetBoolean());
        Assert.True(review.RootElement.GetProperty("oneEducationalMessagePerImage").GetBoolean());
    }

    [Theory]
    [InlineData("landscape", 1920, 1080)]
    [InlineData("portrait", 1080, 1920)]
    public async Task GenerateGeminidsGalleryAsync_RespectsAspectWithoutCropping(string name, int width, int height)
    {
        var root = Path.Combine(Path.GetTempPath(), $"astropulse-gallery-{Guid.NewGuid():N}");
        var service = new AstroPulseGalleryService();

        var result = await service.GenerateGeminidsGalleryAsync(root, new AstroPulseGalleryAspect(name, width, height), CancellationToken.None);

        var info = Image.Identify(result.ImagePaths[0]);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
    }
}
