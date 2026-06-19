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


    [Fact]
    public async Task GenerateAsync_ForMeteorShower_UsesMeteorContextAndDoesNotFailOnConjunctionForbiddenList()
    {
        var planRoot = Path.Combine(_outputRoot, "geminids-plan");
        Directory.CreateDirectory(Path.Combine(planRoot, "plan-input"));
        Directory.CreateDirectory(Path.Combine(planRoot, "question-engine"));
        await File.WriteAllTextAsync(Path.Combine(planRoot, "plan-input", "production-event-intelligence.json"), """
        {
          "planId": "geminids-2026-plan",
          "eventType": "MeteorShower",
          "title": "Geminids meteor shower",
          "storyTheme": "Geminids peak night",
          "visualTheme": "meteor streaks over dark open sky",
          "skyGuideTheme": "East to overhead after 10 PM",
          "skyDirectionHint": "East to overhead after 10 PM",
          "bestViewingWindowLocal": "midnight to pre-dawn",
          "eventDate": "Dec 13–14, 2026",
          "requiredVisualObjects": ["Geminids", "meteor shower", "meteor streaks", "radiant guide", "East to overhead after 10 PM", "midnight to pre-dawn", "Dec 13–14, 2026"],
          "forbiddenTerms": ["Venus", "Jupiter", "conjunction", "after sunset", "look west"]
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(planRoot, "question-engine", "question-driven-narration-v2.json"), """
        {
          "scenes": [
            { "narrationText": "The Geminids meteor shower peaks on Dec 13–14, 2026 with bright meteor streaks." },
            { "narrationText": "Use the radiant guide and scan east to overhead after 10 PM." },
            { "narrationText": "The best window runs midnight to pre-dawn from a dark open sky." }
          ]
        }
        """);

        var service = new SceneAssetsV3Service(
            Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }),
            new DisabledAICinematicImageGenerator(),
            NullLogger<SceneAssetsV3Service>.Instance);

        var result = await service.GenerateAsync(new SceneAssetsV3Request(planRoot, GenerateShort: true, GenerateLong: false, OverwriteExisting: true), CancellationToken.None);

        var timeline = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "visual-timeline-v3.json"));
        Assert.Contains("Geminids", timeline);
        Assert.Contains("meteor shower", timeline);
        Assert.Contains("meteor streaks", timeline);
        Assert.Contains("East to overhead after 10 PM", timeline);
        Assert.Contains("midnight to pre-dawn", timeline);
        Assert.Contains("Dec 13–14, 2026", timeline);
        Assert.DoesNotContain("Jupiter", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Venus", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planet conjunction", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("look west", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("western sky after sunset", timeline, StringComparison.OrdinalIgnoreCase);

        var validation = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "scene-v3-validation.json"));
        Assert.Contains("\"status\": \"Passed\"", validation);
        Assert.Contains("\"forbiddenTermsDetected\": []", validation);

        var diagnostics = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "visual-prompt-diagnostics.json"));
        Assert.Contains("currentPlanId", diagnostics);
        Assert.Contains("currentEventType", diagnostics);
        Assert.Contains("\"sceneGuideType\": \"MeteorShower\"", diagnostics);
        Assert.Contains("\"guideRenderer\": \"DeterministicMeteorShowerGuideRenderer\"", diagnostics);
        Assert.Contains("\"eventType\": \"MeteorShower\"", diagnostics);
        Assert.Contains("guideElementsUsed", diagnostics);
        Assert.Contains("radiant", diagnostics);
        Assert.Contains("meteor streak directions", diagnostics);
        Assert.Contains("forbiddenTermsSource", diagnostics);
        Assert.Contains("allowedGuidanceTerms", diagnostics);
        Assert.Contains("blockedTermsMatched", diagnostics);
        Assert.Contains("staleContextDetected", diagnostics);
        Assert.Contains("staleContextSource", diagnostics);

        var metadata = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "scene-timeline-metadata.json"));
        Assert.Contains("\"sceneGuideType\": \"MeteorShower\"", metadata);
        Assert.Contains("radiant", metadata);
        Assert.Contains("meteor streak directions", metadata);
        using var metadataJson = System.Text.Json.JsonDocument.Parse(metadata);
        var guideScene = metadataJson.RootElement.GetProperty("scenes").EnumerateArray().Single(s => s.GetProperty("renderMode").GetString() == "AccurateSkyGuideScene");
        var guideElements = guideScene.GetProperty("guideElementsUsed").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(guideElements, e => e == "radiant" || e == "meteor streak directions");
        Assert.DoesNotContain("primary", guideElements);
        Assert.DoesNotContain("secondary", guideElements);
        Assert.DoesNotContain("alignment", guideElements);
    }

    [Fact]
    public async Task GenerateAsync_AccurateSkyGuideV2Enabled_CallsProviderForOnlyGuideScenesAndWritesDiagnostics()
    {
        var generator = new CapturingAICinematicImageGenerator(createImage: true);
        var service = new SceneAssetsV3Service(
            Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot, EnableAccurateSkyGuideV2 = true }),
            generator,
            NullLogger<SceneAssetsV3Service>.Instance);

        var result = await service.GenerateAsync(new SceneAssetsV3Request(GenerateShort: true, GenerateLong: false, OverwriteExisting: true), CancellationToken.None);

        Assert.Contains(generator.Requests, r => r.AssetCode == "003-accurate-sky-guide" && r.Prompt.Contains("Accurate Sky Guide", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(generator.Requests, r => r.AssetCode == "004-viewing-tip" && r.Prompt.Contains("Accurate Sky Guide", StringComparison.OrdinalIgnoreCase));
        var diagnostics = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "accurate-sky-guide-v2-diagnostics.json"));
        Assert.Contains("\"enabled\": true", diagnostics);
        Assert.Contains("\"sceneId\": \"003-accurate-sky-guide\"", diagnostics);
        Assert.Contains("\"providerCalled\": true", diagnostics);
        Assert.Contains("\"fallbackUsed\": false", diagnostics);
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "003-accurate-sky-guide-accurate-sky-guide-v2-prompt.txt")));
    }

    [Fact]
    public async Task GenerateAsync_AccurateSkyGuideV2Enabled_FallsBackWhenProviderFails()
    {
        var service = new SceneAssetsV3Service(
            Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot, EnableAccurateSkyGuideV2 = true }),
            new CapturingAICinematicImageGenerator(createImage: false),
            NullLogger<SceneAssetsV3Service>.Instance);

        var result = await service.GenerateAsync(new SceneAssetsV3Request(GenerateShort: true, GenerateLong: false, OverwriteExisting: true), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "003-accurate-sky-guide.png")));
        var diagnostics = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "accurate-sky-guide-v2-diagnostics.json"));
        Assert.Contains("\"providerCalled\": true", diagnostics);
        Assert.Contains("\"fallbackUsed\": true", diagnostics);
        Assert.Contains("\"imageExists\": true", diagnostics);
    }

    private sealed class CapturingAICinematicImageGenerator(bool createImage) : IAICinematicImageGenerator
    {
        public List<AICinematicAssetRequest> Requests { get; } = new();
        public bool IsConfigured => true;
        public string DeploymentName => "test-image2";

        public async Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (!createImage)
            {
                return new AICinematicProviderResult("Failed", null, ProviderConfigured: true, ["test failure"]);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.PlannedImagePath) ?? ".");
            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(32, 32, SixLabors.ImageSharp.Color.Navy);
            await image.SaveAsPngAsync(request.PlannedImagePath, cancellationToken);
            return new AICinematicProviderResult("Generated", request.PlannedImagePath, ProviderConfigured: true, []);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot)) Directory.Delete(_outputRoot, recursive: true);
    }
}
