using Astronomy.MediaFactory.Api;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class DynamicSplitScenePlanResolverTests
{
    [Fact]
    public void Resolve_Uses_SourceScenePlan_When_DynamicSplitSceneCode_Missing()
    {
        var sourcePlan = new WeeklyScenePlan("hero_western_grouping_scene", "hero_western_grouping_scene", 1, "Grouping", "Hybrid", "Hero", new DateOnly(2026, 5, 21), DateTime.UtcNow, ["VENUS", "JUPITER"], 8, "desc", "slow-pan", "cam", [], "cut", "cut", false, "Hero", [], true, false, false);
        var scenePlans = new Dictionary<string, WeeklyScenePlan>(StringComparer.OrdinalIgnoreCase)
        {
            [sourcePlan.SceneCode] = sourcePlan
        };

        var split = new GeneratedSplitSceneMetadata(
            "western_planet_grouping_scene",
            "hero_western_grouping_scene",
            ["VENUS", "JUPITER"],
            "VENUS",
            "Grouping",
            "Hero",
            8,
            new DateOnly(2026, 5, 21),
            DateTime.UtcNow,
            "/tmp/scripts/western_planet_grouping_scene.ssc",
            "/tmp/scenes/western_planet_grouping_scene.png");
        var splitMap = new Dictionary<string, GeneratedSplitSceneMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            [split.SceneCode] = split
        };

        var resolved = DynamicSplitScenePlanResolver.Resolve(split.SceneCode, scenePlans, splitMap, out var sourceSceneCode, out var metadataSource);

        Assert.NotNull(resolved);
        Assert.Equal(sourcePlan.SceneCode, resolved!.SceneCode);
        Assert.Equal("hero_western_grouping_scene", sourceSceneCode);
        Assert.Equal("source-scene-plan", metadataSource);
    }
}
