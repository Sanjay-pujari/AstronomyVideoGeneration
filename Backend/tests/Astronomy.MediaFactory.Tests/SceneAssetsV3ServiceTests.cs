using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class SceneAssetsV3ServiceTests : IDisposable
{
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "scene-assets-v3-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_WritesNarrationAlignedShortAndLongSceneV3Packages()
    {
        var service = new SceneAssetsV3Service(
            Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }),
            new DisabledAICinematicImageGenerator(),
            NullLogger<SceneAssetsV3Service>.Instance);

        var result = await service.GenerateAsync(new SceneAssetsV3Request(OverwriteExisting: true), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "visual-timeline-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "long", "visual-timeline-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "scene-manifest-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "long", "scene-manifest-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "scene-timeline-metadata.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "long", "scene-timeline-metadata.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "003-accurate-sky-guide.png")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "long", "006-accurate-sky-guide.png")));

        var shortManifest = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "scene-manifest-v3.json"));
        Assert.Contains("\"sceneCount\": 5", shortManifest);
        Assert.Contains("AccurateSkyGuideScene", shortManifest);
        Assert.Contains("Generic", shortManifest);

        var longReview = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "long", "scene-review-v3.json"));
        Assert.Contains("\"sceneCount\": 9", longReview);
        Assert.Contains("\"accurateSkyGuidePresent\": true", longReview);
        Assert.Contains("\"duplicateHashDetected\": false", longReview);
        Assert.Contains("\"sameBackgroundDetected\": false", longReview);
        Assert.Contains("\"sameCompositionDetected\": false", longReview);
        Assert.Contains("\"sameCameraAngleDetected\": false", longReview);
        Assert.Contains("\"status\": \"Passed\"", longReview);

        var shortMetadata = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "scene-timeline-metadata.json"));
        Assert.Contains("\"recommendedTransition\":", shortMetadata);
        Assert.Contains("\"recommendedMotion\":", shortMetadata);
    }

    [Fact]
    public async Task GenerateAsync_ForPlanetConjunction_WritesConjunctionTimelineAndRejectsForbiddenMeteorTerms()
    {
        var planRoot = Path.Combine(_outputRoot, "planet-conjunction-plan");
        Directory.CreateDirectory(Path.Combine(planRoot, "plan-input"));
        Directory.CreateDirectory(Path.Combine(planRoot, "question-engine"));
        await File.WriteAllTextAsync(Path.Combine(planRoot, "plan-input", "production-event-intelligence.json"), """
        {
          "eventType": "PLANET_CONJUNCTION",
          "storyTheme": "closest evening pairing",
          "visualTheme": "twilight conjunction guide",
          "skyGuideTheme": "western sky after sunset",
          "requiredVisualObjects": ["Venus", "Jupiter", "two bright planets close together", "western sky after sunset", "twilight horizon", "angular separation 1.63 degrees"],
          "forbiddenTerms": ["Geminids", "meteor", "meteor shower", "radiant", "Phaethon", "debris stream"]
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(planRoot, "question-engine", "question-driven-narration-v2.json"), """
        {
          "scenes": [
            { "narrationText": "Venus and Jupiter form a close conjunction after sunset." },
            { "narrationText": "The two bright planets fit together above the western twilight horizon." }
          ]
        }
        """);
        Directory.CreateDirectory(Path.Combine(planRoot, "scene-assets-v3", "short"));
        await File.WriteAllTextAsync(Path.Combine(planRoot, "scene-assets-v3", "short", "stale.txt"), "stale");

        var service = new SceneAssetsV3Service(
            Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }),
            new DisabledAICinematicImageGenerator(),
            NullLogger<SceneAssetsV3Service>.Instance);

        var result = await service.GenerateAsync(new SceneAssetsV3Request(planRoot, GenerateShort: true, GenerateLong: false, OverwriteExisting: true), CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(result.OutputRoot, "short", "stale.txt")));
        var timeline = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "visual-timeline-v3.json"));
        Assert.Contains("Jupiter", timeline);
        Assert.Contains("Venus", timeline);
        Assert.Contains("two bright planets close together", timeline);
        Assert.Contains("western sky after sunset", timeline);
        Assert.Contains("twilight horizon", timeline);
        Assert.Contains("angular separation 1.63 degrees", timeline);
        Assert.DoesNotContain("Geminids", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meteor", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("radiant", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Phaethon", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debris stream", timeline, StringComparison.OrdinalIgnoreCase);

        var diagnostics = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "scene-assets-v3-diagnostics.json"));
        Assert.Contains("finalVisualPrompt", diagnostics);
        Assert.Contains("forbiddenTermsDetected", diagnostics);
        Assert.Contains("azureCallsCount", diagnostics);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot)) Directory.Delete(_outputRoot, recursive: true);
    }
}
