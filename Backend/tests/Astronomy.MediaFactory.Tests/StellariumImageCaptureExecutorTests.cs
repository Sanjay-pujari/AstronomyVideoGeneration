using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumImageCaptureExecutorTests
{
    [Fact]
    public async Task DryRun_UsesConfiguredScriptAndCaptureDirectories()
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), $"stellarium-captures-{Guid.NewGuid():N}");
        var scriptsRoot = Path.Combine(Path.GetTempPath(), $"stellarium-scripts-{Guid.NewGuid():N}");
        var options = Options.Create(new StellariumOptions { CaptureDirectory = captureRoot, ScriptsDirectory = scriptsRoot, Enabled = true });
        var sut = new StellariumImageCaptureExecutor(options, new StellariumScriptGenerator(options), NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();

        var result = await sut.CaptureAsync(BuildPlan(planId), new StellariumCaptureExecutionRequest(planId, DryRun: true), CancellationToken.None);

        Assert.All(result.Images, image =>
        {
            Assert.StartsWith(Path.Combine(captureRoot, "content-plans", planId.ToString(), "stellarium-scenes"), image.ImagePath!, StringComparison.Ordinal);
            Assert.StartsWith(Path.Combine(scriptsRoot, "content-plans", planId.ToString()), image.ScriptPath!, StringComparison.Ordinal);
            Assert.Contains(Path.GetFileNameWithoutExtension(image.ImagePath!), File.ReadAllText(image.ScriptPath!));
        });
    }

    [Fact]
    public async Task Disabled_DoesNotExecute_AndReturnsClearError()
    {
        var options = Options.Create(new StellariumOptions { Enabled = false, CaptureDirectory = Path.GetTempPath(), ScriptsDirectory = Path.GetTempPath() });
        var sut = new StellariumImageCaptureExecutor(options, new StellariumScriptGenerator(options), NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();

        var result = await sut.CaptureAsync(BuildPlan(planId), new StellariumCaptureExecutionRequest(planId), CancellationToken.None);

        Assert.All(result.Images, i => Assert.Equal("Stellarium capture is disabled in configuration.", i.ErrorMessage));
    }

    [Fact]
    public async Task MissingExecutable_ReturnsClearError_AndFailsScene()
    {
        var options = Options.Create(new StellariumOptions { Enabled = true, ExecutablePath = Path.Combine(Path.GetTempPath(), "missing-stellarium.exe"), CaptureDirectory = Path.GetTempPath(), ScriptsDirectory = Path.GetTempPath() });
        var sut = new StellariumImageCaptureExecutor(options, new StellariumScriptGenerator(options), NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();
        var result = await sut.CaptureAsync(BuildPlan(planId), new StellariumCaptureExecutionRequest(planId), CancellationToken.None);
        Assert.All(result.Images, i => Assert.Contains("executable was not found", i.ErrorMessage!, StringComparison.OrdinalIgnoreCase));
        Assert.False(result.Success);
    }

    private static StellariumSceneCapturePlan BuildPlan(Guid planId) => new(
        planId, "DailySkyGuide", "us", "x", 1, 2, "UTC", DateOnly.FromDateTime(DateTime.UtcNow),
        [new("DailySkyGuide_Target", "ObjectFocus", "Target", "mars", "Mars", DateTime.UtcNow, "Focus", 35, true, true, true, false, true, "TargetHighlight", 2, null)], []);
}
