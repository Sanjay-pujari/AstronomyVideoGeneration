using Astronomy.MediaFactory.Api;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public class WeeklySscSceneFinalizerTests
{
    [Fact]
    public void Build_DeduplicatesBySceneCode_AndMergesSourceSceneCodes()
    {
        var result = WeeklySscSceneFinalizer.Build(
            "/tmp/scripts",
            "/tmp/scenes",
            new[]
            {
                ("western_planet_grouping_scene", (IEnumerable<string>)new[] { "hero_western_grouping_scene" }),
                ("moon_hero_scene", (IEnumerable<string>)new[] { "hero_western_grouping_scene" }),
                ("western_planet_grouping_scene", (IEnumerable<string>)new[] { "best_night_wide_scene" }),
                ("moon_hero_scene", (IEnumerable<string>)new[] { "best_night_wide_scene" })
            });

        Assert.Collection(result,
            western =>
            {
                Assert.Equal("moon_hero_scene", western.SceneCode);
                Assert.Equal("/tmp/scripts/moon_hero_scene.ssc", western.ScriptPath.Replace('\\', '/'));
                Assert.Contains("hero_western_grouping_scene", western.SourceSceneCodes);
                Assert.Contains("best_night_wide_scene", western.SourceSceneCodes);
            },
            moon =>
            {
                Assert.Equal("western_planet_grouping_scene", moon.SceneCode);
                Assert.Equal("/tmp/scripts/western_planet_grouping_scene.ssc", moon.ScriptPath.Replace('\\', '/'));
                Assert.Contains("hero_western_grouping_scene", moon.SourceSceneCodes);
                Assert.Contains("best_night_wide_scene", moon.SourceSceneCodes);
            });

        Assert.DoesNotContain(result, x => x.SceneCode == "hero_western_grouping_scene");
    }
}
