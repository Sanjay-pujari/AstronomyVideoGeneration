using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
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

        var promptJson = await File.ReadAllTextAsync(Path.Combine(root, "thumbnail-prompt.json"));
        Assert.Contains("background-only image", promptJson);
        Assert.Contains("HeroGalleryEventVisualLogic", promptJson);
        Assert.False(promptJson.Contains("Mercury", StringComparison.OrdinalIgnoreCase));
        Assert.False(promptJson.Contains("thumbnail-review.json", StringComparison.OrdinalIgnoreCase));
    }
}
