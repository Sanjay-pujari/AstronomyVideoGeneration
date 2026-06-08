using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class InfographicLayoutBlueprintGeneratorTests
{
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateInfographicLayoutBlueprintAsync_GoldenPilotWritesSixValidatedBlueprintsWithoutRenderingMedia()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var generator = new InfographicLayoutBlueprintGenerator(
            Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
            NullLogger<InfographicLayoutBlueprintGenerator>.Instance);

        var result = await generator.GenerateInfographicLayoutBlueprintAsync(new InfographicLayoutBlueprintRequest(
            EventId,
            RegionId,
            "en",
            "CasualSkyWatcher",
            "Beginner",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(EventId, result.EventId);
        Assert.Equal(6, result.SceneCount);
        Assert.True(result.IsValid);
        Assert.Equal(6, result.LayoutBlueprints.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("no images", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("Constellation", StringComparison.OrdinalIgnoreCase));

        AssertScene(result.LayoutBlueprints[0], 1, "WHAT", "AstronomyMagazineCover", 90, 10, "celestialObjects");
        AssertScene(result.LayoutBlueprints[1], 2, "WHERE", "ObservationChart", 80, 20, "skyGuidance");
        AssertScene(result.LayoutBlueprints[2], 3, "WHEN", "TimelineInfographic", 80, 20, "educationalLayer");
        AssertScene(result.LayoutBlueprints[3], 4, "HOW", "ObservationGuide", 75, 25, "skyGuidance");
        AssertScene(result.LayoutBlueprints[4], 5, "WHY", "SignificanceGraphic", 80, 20, "educationalLayer");
        AssertScene(result.LayoutBlueprints[5], 6, "ACTION", "AstronomyPoster", 90, 10, "celestialObjects");

        var where = result.LayoutBlueprints[1];
        Assert.NotEmpty(where.LayoutZones.HorizonZone);
        Assert.NotEmpty(where.LayoutZones.AltitudeGuideZone);
        Assert.Empty(where.LayoutZones.ConstellationZone);
        Assert.Empty(where.LayoutZones.ReferenceStarZone);

        var when = result.LayoutBlueprints[2];
        Assert.NotEmpty(when.LayoutZones.TimelineZone);
        Assert.NotEmpty(when.LayoutZones.ViewingWindowZone);

        var how = result.LayoutBlueprints[3];
        Assert.NotEmpty(how.LayoutZones.StepZone);
        Assert.Empty(how.LayoutZones.ConstellationZone);
        Assert.Empty(how.LayoutZones.ReferenceStarZone);

        Assert.NotEmpty(result.LayoutBlueprints[4].LayoutZones.SignificanceZone);
        Assert.NotEmpty(result.LayoutBlueprints[5].LayoutZones.CtaZone);
        Assert.All(result.LayoutBlueprints, blueprint =>
        {
            Assert.NotEmpty(blueprint.LayoutZones.HeroZone);
            Assert.True(blueprint.TextCoveragePercent <= 25);
            Assert.True(blueprint.VisualCoveragePercent >= 75);
            Assert.Contains("powerpoint_card", blueprint.ForbiddenPatterns);
            Assert.Contains("fake_circle_planets", blueprint.ForbiddenPatterns);
        });
        Assert.Equal(6, result.LayoutBlueprints.Select(x => x.LayoutTemplate).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var outputPath = Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "layout-blueprints", "scene-001-layout-blueprint.json");
        Assert.True(File.Exists(outputPath));
        var outputJson = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("AstronomyMagazineCover", outputJson);
        Assert.DoesNotContain("finalImagePath", outputJson, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertScene(InfographicLayoutBlueprint blueprint, int sceneNumber, string sceneKey, string template, int visualCoverage, int textCoverage, string requiredLayer)
    {
        Assert.Equal(sceneNumber, blueprint.SceneNumber);
        Assert.Equal(sceneKey, blueprint.SceneKey);
        Assert.Equal(template, blueprint.LayoutTemplate);
        Assert.Equal(visualCoverage, blueprint.VisualCoveragePercent);
        Assert.Equal(textCoverage, blueprint.TextCoveragePercent);
        Assert.Contains(requiredLayer, blueprint.RequiredLayers);
    }

    private static async Task WriteRequiredInputsAsync(string workingDirectory)
    {
        var root = Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "question-answer-set.json"), JsonSerializer.Serialize(new { eventId = EventId, regionId = RegionId }));
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.enriched.json"), JsonSerializer.Serialize(new { eventId = EventId, sceneCount = 6 }));
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-narration.json"), JsonSerializer.Serialize(new { eventId = EventId, sceneCount = 6 }));
    }

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "infographic-layout-blueprint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
