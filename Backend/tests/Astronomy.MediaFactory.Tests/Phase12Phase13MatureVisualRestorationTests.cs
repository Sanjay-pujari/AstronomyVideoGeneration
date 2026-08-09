namespace Astronomy.MediaFactory.Tests;

public sealed class Phase12Phase13MatureVisualRestorationTests
{
    [Fact]
    public void Phase12GeneratesOneAzureBackgroundPerAspectAndNeverStretches()
    {
        var source = Source("Astronomy.MediaFactory.Infrastructure", "Persistence", "MatureThumbnailCandidatePublisher.cs");
        Assert.Contains("providerCallCount = 3", source);
        Assert.Contains("1024x1024", source);
        Assert.Contains("Mode = ResizeMode.Crop", source);
        Assert.Contains("stretchResizeUsed = false", source);
    }

    [Fact]
    public void Phase12FactsAreDeterministicAndPromptForbidsEmbeddedText()
    {
        var source = Source("Astronomy.MediaFactory.Infrastructure", "Persistence", "MatureThumbnailCandidatePublisher.cs");
        Assert.Contains("factualTextRenderedDeterministically = true", source);
        Assert.Contains("NO embedded text. NO labels. NO numbers. NO watermark", source);
        Assert.Contains("RadiantBurstThumbnail", source);
    }

    [Fact]
    public void Phase12DoesNotRequirePhase8OrPhase11Raster()
    {
        var source = Source("Astronomy.MediaFactory.Infrastructure", "Persistence", "MatureThumbnailCandidatePublisher.cs");
        Assert.DoesNotContain("08-scene-assets", source);
        Assert.DoesNotContain("11-hero", source);
        Assert.Contains("sourcePhase8PhysicalPath = \"\"", source);
    }

    [Fact]
    public void Phase13CreatesSixIndependentLandscapeBackgrounds()
    {
        var source = Source("Astronomy.MediaFactory.Rendering", "Phase13GalleryAuthority.cs");
        Assert.Contains("for (var index = 0; index < 6; index++)", source);
        Assert.Contains("providerCallCount = 6", source);
        Assert.Contains("physical.Width == 1920 && physical.Height == 1080", source);
        Assert.Contains("backgroundHashes.Add", source);
    }

    [Fact]
    public void Phase13PromptsHaveNoAiTextAndOverlayAddsPublicFurniture()
    {
        var authority = Source("Astronomy.MediaFactory.Rendering", "Phase13GalleryAuthority.cs");
        var renderer = Source("Astronomy.MediaFactory.Rendering", "AstroPulseGalleryService.cs");
        Assert.Contains("NO embedded text. NO labels. NO captions. NO numbers. NO watermark. NO UI typography", authority);
        Assert.Contains("embeddedAiTextRequested = false", authority);
        Assert.Contains("Drashyam Astronomy", renderer);
        Assert.Contains("{slot:00}/06", renderer);
    }

    [Fact]
    public void Phase13DoesNotUsePhase8OrPhase10RasterAuthority()
    {
        var active = Source("Astronomy.MediaFactory.Rendering", "Phase13GalleryAuthority.cs")
            .Split("private static (SceneAssetManifestItem", StringSplitOptions.None)[0];
        Assert.DoesNotContain("08-scene-assets", active);
        Assert.DoesNotContain("10-scene-validation", active);
        Assert.Contains("phase8RasterUsed = false", active);
        Assert.Contains("phase10RasterUsed = false", active);
    }

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astronomy.MediaFactory.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(new[] { directory?.FullName ?? throw new DirectoryNotFoundException(), "src" }.Concat(parts).ToArray()));
    }
}
