using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailV7CinematicOverlayRendererTests
{
    [Fact]
    public async Task RenderAsync_JupiterVenus_UsesCinematicOverlayDiagnosticsWithoutMercuryOrReviewRequirement()
    {
        var root = Path.Combine(Path.GetTempPath(), "thumb-v7-" + Guid.NewGuid().ToString("N"));
        var request = new ThumbnailAssetGenerationRequest
        {
            EventId = "jupiter-venus-conjunction",
            RegionId = "US",
            DryRun = false,
            OverwriteExisting = true,
            ProductionContext = new ProductionPipelineExecutionContext(
                true, null, null, null, false,
                EventType: "PlanetConjunction",
                ProductionEventIntelligence: new ProductionEventIntelligence(
                    "Astronomy",
                    "PlanetConjunction",
                    "Jupiter and Venus Conjunction",
                    "Jupiter + Venus",
                    new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero),
                    null,
                    "7:10 PM",
                    "After sunset",
                    "low western horizon",
                    "United States",
                    ["Jupiter", "Venus", "Mercury"],
                    [],
                    "Excellent",
                    "Low",
                    12,
                    null,
                    [], [], [], [], [],
                    ResolvedObjectNames: ["Jupiter", "Venus", "Mercury"]))
        };

        var result = await new ThumbnailV7CinematicOverlayRenderer().RenderAsync(request, root, overwriteExisting: true, CancellationToken.None);

        Assert.Contains(result.OutputFiles, p => p.EndsWith("thumbnail-final.png"));
        Assert.Contains(result.OutputFiles, p => p.EndsWith("thumbnail-landscape.png"));
        Assert.Contains(result.OutputFiles, p => p.EndsWith("thumbnail-portrait.png"));
        Assert.Contains(result.OutputFiles, p => p.EndsWith("thumbnail-square.png"));
        Assert.All(new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" }, file => Assert.True(File.Exists(Path.Combine(root, file)), file));

        using var diagnosticsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "thumbnail-v7-diagnostics.json")));
        var diagnostics = diagnosticsDocument.RootElement;
        Assert.Equal("V7", diagnostics.GetProperty("thumbnailVersion").GetString());
        Assert.Equal("ThumbnailV7CinematicOverlayRenderer", diagnostics.GetProperty("selectedRenderer").GetString());
        Assert.Equal("HeroGalleryEventVisualLogic", diagnostics.GetProperty("backgroundPromptSource").GetString());
        Assert.False(diagnostics.GetProperty("v6RendererExecuted").GetBoolean());
        Assert.False(diagnostics.GetProperty("manualCelestialAssetPlacement").GetBoolean());
        Assert.False(diagnostics.GetProperty("extraObjectsDetected").GetBoolean());
        Assert.False(diagnostics.GetProperty("mercuryAppears").GetBoolean());
        Assert.True(diagnostics.GetProperty("oldValidationBlocked").GetBoolean());
        Assert.False(diagnostics.GetProperty("thumbnailReviewJsonRequired").GetBoolean());
        Assert.Equal("PerVariantAzureImage2", diagnostics.GetProperty("backgroundMode").GetString());
        Assert.False(diagnostics.GetProperty("cropFromLandscape").GetBoolean());
        Assert.True(diagnostics.GetProperty("vectorIconsUsed").GetBoolean());
        Assert.False(diagnostics.GetProperty("emojiIconsUsed").GetBoolean());
        Assert.EndsWith("v7-background-landscape.png", diagnostics.GetProperty("landscapeBackgroundPath").GetString());
        Assert.EndsWith("v7-background-portrait.png", diagnostics.GetProperty("portraitBackgroundPath").GetString());
        Assert.EndsWith("v7-background-square.png", diagnostics.GetProperty("squareBackgroundPath").GetString());

        var promptJson = await File.ReadAllTextAsync(Path.Combine(root, "thumbnail-prompt.json"));
        Assert.Contains("background-only image", promptJson);
        Assert.Contains("1080x1920", promptJson);
        Assert.Contains("no cropping from landscape", promptJson);
        Assert.Contains("HeroGalleryEventVisualLogic", promptJson);
        Assert.False(promptJson.Contains("Mercury", StringComparison.OrdinalIgnoreCase));
        Assert.False(promptJson.Contains("thumbnail-review.json", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public async Task RenderAsync_WithProvider_WritesRawVerificationArtifactsPerAspect()
    {
        var root = Path.Combine(Path.GetTempPath(), "thumb-v7-provider-" + Guid.NewGuid().ToString("N"));
        var request = new ThumbnailAssetGenerationRequest
        {
            EventId = "geminid-meteor-shower",
            RegionId = "US",
            DryRun = false,
            OverwriteExisting = true,
            ProductionContext = new ProductionPipelineExecutionContext(
                true, null, null, null, false,
                EventType: "MeteorShower",
                ProductionEventIntelligence: new ProductionEventIntelligence(
                    "Astronomy", "MeteorShower", "Geminid Meteor Shower", "Geminids", new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero), null,
                    "2:00 AM", "After midnight", "northeast sky", "United States", ["Radiant"], [], "Excellent", "Low", null, null,
                    [], [], [], [], [], ResolvedObjectNames: ["Radiant"]))
        };

        var result = await new ThumbnailV7CinematicOverlayRenderer(
            azureImage2Generator: async (generationRequest, ct) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(generationRequest.RawOutputPath)!);
                using var image = new Image<Rgba32>(generationRequest.RequestedWidth, generationRequest.RequestedHeight, Color.DarkBlue);
                await image.SaveAsPngAsync(generationRequest.RawOutputPath, ct);
                return new ThumbnailV7AzureImage2GenerationResult(true, true, 11, 3, "TestProvider", "TestModel", $"req-{generationRequest.Aspect}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 14, null);
            }).RenderAsync(request, root, overwriteExisting: true, CancellationToken.None);

        Assert.Contains(result.OutputFiles, p => p.EndsWith("thumbnail-generation-metadata.json"));
        Assert.Contains(result.OutputFiles, p => p.EndsWith("thumbnail-processing-log.json"));
        Assert.Contains(result.OutputFiles, p => p.EndsWith("thumbnail-prompt-diff.md"));
        Assert.All(new[] { "thumbnail-landscape-raw.png", "thumbnail-square-raw.png", "thumbnail-portrait-raw.png", "thumbnail-generation-metadata.json", "thumbnail-processing-log.json", "thumbnail-prompt-diff.md" }, file => Assert.True(File.Exists(Path.Combine(root, file)), file));

        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "thumbnail-generation-metadata.json")));
        Assert.Equal("PASS", metadata.RootElement.GetProperty("validation").GetProperty("status").GetString());
        Assert.Equal(3, metadata.RootElement.GetProperty("providerCalls").GetArrayLength());
        Assert.Contains("Hashes unique: true", await File.ReadAllTextAsync(Path.Combine(root, "thumbnail-prompt-diff.md")));
    }

}
