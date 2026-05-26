using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklyStellariumScriptWriterTests
{
    [Fact]
    public async Task WriteAsync_Writes_Scripts_And_Diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "weekly-ssc-" + Guid.NewGuid().ToString("N"));
        var writer = new WeeklyStellariumScriptWriter(Options.Create(new StellariumOptions()));
        var shot = new WeeklyCinematicShot(
            "s3_multi_object_grouping_01",
            "wide_group_reveal",
            "purpose",
            ["MOON"],
            "MOON",
            new DateOnly(2026, 5, 25),
            new TimeOnly(22, 0),
            10,
            "S",
            45,
            50,
            40,
            new WeeklyCameraMovementPlan("zoom", "smooth", "in", 50, 40, false),
            new WeeklyShotTransitionPlan("cut", "fade", "in"),
            new WeeklyShotTransitionPlan("fade", "cut", "out"),
            "cinematic",
            "",
            "",
            "",
            ["core.setDate('2026-05-25T22:00:00')", "core.selectObjectByName('MOON')"],
            new WeeklyShotNarrationSync("beat", 0, 10, "purpose", null, []));

        var pkg = new WeeklyCinematicShotPackage(true, "story", "run", 1, 1, 10, [new WeeklyCinematicSceneSequence("seg", "scene", "type", "src", 10, [shot], "purpose", new WeeklyShotTransitionPlan("cut", "cut", ""), new WeeklyShotTransitionPlan("cut", "cut", ""))], [], [], []);

        var result = await writer.WriteAsync(pkg, root, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Single(result.Scripts);
        Assert.True(File.Exists(result.Scripts[0].ScriptPath));
        var scriptText = await File.ReadAllTextAsync(result.Scripts[0].ScriptPath);
        Assert.Contains("// Shot: s3_multi_object_grouping_01", scriptText);
        Assert.Contains("core.screenshot('", scriptText);
        Assert.True(File.Exists(result.DiagnosticsPath));
    }
}
