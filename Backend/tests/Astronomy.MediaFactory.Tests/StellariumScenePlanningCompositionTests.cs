using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Xunit;
using ContractsSceneGenerationMode = Astronomy.MediaFactory.Contracts.SceneGenerationMode;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumScenePlanningCompositionTests
{
    [Fact]
    public async Task CompositionFocused_GroupsVisibleObjects_AndReducesSceneCount()
    {
        var options = Options.Create(new StellariumOptions { DailySkyGuideSceneGenerationMode = ContractsSceneGenerationMode.CompositionFocused, MaxCompositionScenes = 5 });
        var sut = new DailySkyGuideStellariumScenePlanner(options);
        var plan = BuildPlan();
        var visibility = BuildVisibility();

        var result = await sut.BuildScenePlanAsync(plan, visibility, CancellationToken.None);

        Assert.All(result.Scenes, s => Assert.Equal("Composition", s.SceneType));
        Assert.True(result.Scenes.Count <= 5);
        Assert.Contains(result.Scenes, s => s.Metadata is not null && s.Metadata.ContainsKey("IncludedObjects"));
    }

    [Fact]
    public async Task ObjectFocused_PreservesLegacyFocusedScenes()
    {
        var options = Options.Create(new StellariumOptions { DailySkyGuideSceneGenerationMode = ContractsSceneGenerationMode.ObjectFocused });
        var sut = new DailySkyGuideStellariumScenePlanner(options);

        var result = await sut.BuildScenePlanAsync(BuildPlan(), BuildVisibility(), CancellationToken.None);

        Assert.Contains(result.Scenes, s => s.SceneType == "MoonFocus" || s.SceneType == "ObjectFocus");
        Assert.DoesNotContain(result.Scenes, s => s.SceneType == "Composition");
    }

    [Fact]
    public async Task Hybrid_IncludesCompositionAndFocusedScenes_WithLimits()
    {
        var options = Options.Create(new StellariumOptions { DailySkyGuideSceneGenerationMode = ContractsSceneGenerationMode.Hybrid, MaxCompositionScenes = 5, MaxFocusedScenes = 3 });
        var sut = new DailySkyGuideStellariumScenePlanner(options);

        var result = await sut.BuildScenePlanAsync(BuildPlan(), BuildVisibility(), CancellationToken.None);

        Assert.Contains(result.Scenes, s => s.SceneType == "Composition");
        Assert.Contains(result.Scenes, s => s.SceneType == "MoonFocus" || s.SceneType == "ObjectFocus");
        Assert.True(result.Scenes.Count <= 8);
    }

    private static ContentGenerationPlan BuildPlan() => new()
    {
        ContentCategoryCode = "DailySkyGuide",
        PrimaryCelestialObjectCode = "Mars"
    };

    private static AstronomyVisibilityResult BuildVisibility()
    {
        var now = DateTime.UtcNow;
        return new AstronomyVisibilityResult(
            "us",
            "Phoenix",
            33.4,
            -112.0,
            "America/Phoenix",
            DateOnly.FromDateTime(now),
            now,
            now.AddHours(8),
            now,
            now.AddHours(3),
            "Waxing Gibbous",
            72,
            new List<VisibleCelestialObjectResult>
            {
                BuildObj("Moon","Moon","west",40),
                BuildObj("Venus","Venus","west",30),
                BuildObj("Jupiter","Jupiter","west",35),
                BuildObj("Mars","Mars","south",50),
            }, []);
    }

    private static VisibleCelestialObjectResult BuildObj(string code, string name, string dir, double alt)
        => new(
            code,
            name,
            "Planet",
            true,
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(2),
            DateTime.UtcNow.AddHours(1),
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(2),
            alt / 90d,
            0.9,
            0.8,
            0.8,
            0.9,
            $"Visible toward {dir}");
}
