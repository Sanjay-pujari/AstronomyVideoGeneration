using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public class WeeklySscSceneBuilderTests
{
    [Fact]
    public void GroupingBuild_UsesCanonicalPattern_AndValidationRequirements()
    {
        var builder = new WeeklySscSceneBuilder();
        var tmp = Path.Combine(Path.GetTempPath(), "weekly-group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var shot = new WeeklyCinematicShot(
            "s3_multi_object_grouping_01",
            "wide_group_reveal",
            "purpose",
            ["MOON", "VENUS", "JUPITER", "SATURN"],
            null,
            new DateOnly(2026, 5, 25),
            new TimeOnly(22, 0),
            10,
            "W",
            45,
            45,
            45,
            new WeeklyCameraMovementPlan("static", "static", "W", 45, 45, false),
            new WeeklyShotTransitionPlan("cut", "cut", "in"),
            new WeeklyShotTransitionPlan("cut", "cut", "out"),
            "static",
            Path.Combine(tmp, "shot.png"),
            Path.Combine(tmp, "shot.mp4"),
            Path.Combine(tmp, "s3_multi_object_grouping_01.ssc"),
            [],
            new WeeklyShotNarrationSync("beat", 0, 10, "purpose", null, []));

        var composition = new WeeklySceneCompositionEntry(
            shot.ShotCode,
            "Grouping",
            ["MOON", "VENUS", "JUPITER", "SATURN"],
            ["MOON", "VENUS", "JUPITER", "SATURN"],
            [],
            250.5,
            34.25,
            10,
            8,
            67,
            false,
            4,
            4,
            null,
            false,
            false,
            []);

        var commands = builder.Build(shot, composition);
        var text = string.Join("\n", commands);

        Assert.Contains("core.setGuiVisible(false);", text);
        Assert.Contains("\"Moon\"", text);
        Assert.Contains("\"Venus\"", text);
        Assert.DoesNotContain("\"MOON\"", text);
        Assert.Contains("LabelMgr.deleteAllLabels();", text);
        Assert.Contains("HighlightMgr.cleanHighlightList();", text);
        Assert.Contains("LabelMgr.labelObject(objectName, objectName, true, 20, \"#ffff66\", \"NE\", 15, \"Line\", false, 0);", text);
        Assert.Equal(1, commands.Count(c => c.Contains("core.screenshot(", StringComparison.Ordinal)));
        Assert.DoesNotContain("core.moveToAltAzi(270", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("core.moveToAltAzi(\"35d\", \"260d\", 0);", text);
        Assert.Equal(1, commands.Count(c => c.Contains("core.moveToAltAzi(", StringComparison.Ordinal)));
        Assert.Equal(1, commands.Count(c => c.Contains("StelMovementMgr.zoomTo(", StringComparison.Ordinal)));
        var reportPath = Path.Combine(tmp, "grouped-ssc-validation-report.json");
        Assert.True(File.Exists(reportPath));
    }
}
