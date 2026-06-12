using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyInfographicRendererTests
{
    [Fact]
    public async Task RenderAsync_FailsProductionFullMoonWhenResolvedAssetIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"missing-moon-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var renderer = new AstronomyInfographicRenderer(
            new AstronomyBackgroundLayerRenderer(),
            new CelestialObjectLayerRenderer(),
            new SkyGuidanceLayerRenderer(),
            new EducationalLayerRenderer(),
            new AnnotationLayerRenderer(),
            new EmptyAssetPathResolver(root));

        var spec = new QuestionDrivenVisualSpec(
            "event-1",
            "US-CA",
            "en",
            1,
            "What",
            "OpeningOverview",
            "What is happening?",
            "Snow Moon Full Moon: what to watch.",
            "Watch the Snow Moon rise.",
            "Snow Moon Full Moon: what to watch.",
            6,
            "professional full Moon scene",
            ["Snow Moon"],
            ["drawable-object:Moon phase=FullMoon source=Moon.FullMoon realisticTexture=craters-maria"],
            ["Moon is dominant"],
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            "NamedFullMoon",
            UsesLocalPlanetAssets: false,
            StrategyValidationFacts: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["assetKey"] = "Moon.FullMoon",
                ["realisticObjectRequired"] = "true",
                ["primitivePlaceholderUsed"] = "false",
                ["DebugFallbackEnabled"] = "false"
            },
            DrawableVisualObjects: [new SceneDrawableVisualObject("Moon", "FullMoon", "large/hero-visible", Glow: true, Label: "Snow Moon", Placement: "eastern horizon", AssetKey: "Moon.FullMoon")]);

        var outputPath = Path.Combine(root, "scene-001-final.png");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.RenderAsync(outputPath, spec, string.Empty, string.Empty, CancellationToken.None));

        Assert.Contains("Moon.FullMoon", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Primitive circle fallback is allowed only when DebugFallbackEnabled=true", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EmptyAssetPathResolver(string baseDirectory) : IRuntimeAssetPathResolver
    {
        public string BaseDirectory { get; } = baseDirectory;
        public string ResolveAssetPath(string relativePath) => Path.Combine(BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        public string ResolveFontPath(string relativeFontPath) => ResolveAssetPath(relativeFontPath);
        public string ResolveCelestialAssetPath(string objectKey, string fileName) => Path.Combine(BaseDirectory, "assets", "celestial", objectKey, fileName);
        public string GetAssetsRoot() => Path.Combine(BaseDirectory, "assets");
        public string GetFontsRoot() => Path.Combine(BaseDirectory, "assets", "fonts");
        public string GetCelestialRoot() => Path.Combine(BaseDirectory, "assets", "celestial");
        public bool AssetExists(string relativePath) => File.Exists(ResolveAssetPath(relativePath)) || Directory.Exists(ResolveAssetPath(relativePath));
    }
}
