using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumImageCaptureExecutorTests
{
    [Fact]
    public async Task DryRun_ReturnsExpectedPaths_AndDoesNotCreateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stellarium-capture-tests-{Guid.NewGuid():N}");
        var options = Options.Create(new StellariumOptions { OutputRoot = root, Enabled = true });
        var sut = new StellariumImageCaptureExecutor(options, NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();
        var plan = BuildPlan(planId);

        var result = await sut.CaptureAsync(plan, new StellariumCaptureExecutionRequest(planId, DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.RequestedSceneCount);
        Assert.Equal(0, result.CapturedSceneCount);
        Assert.Contains("DryRun enabled. No images were captured.", result.Warnings);
        Assert.All(result.Images, x => Assert.NotNull(x.ImagePath));
        Assert.False(Directory.Exists(result.OutputFolder!));
    }

    [Fact]
    public async Task DisabledConfig_ReturnsWarning()
    {
        var options = Options.Create(new StellariumOptions { OutputRoot = "outputs/content-plans", Enabled = false });
        var sut = new StellariumImageCaptureExecutor(options, NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();

        var result = await sut.CaptureAsync(BuildPlan(planId), new StellariumCaptureExecutionRequest(planId, DryRun: false), CancellationToken.None);

        Assert.Contains("Stellarium capture is disabled in configuration.", result.Warnings);
    }

    private static StellariumSceneCapturePlan BuildPlan(Guid planId) => new(
        planId, "DailySkyGuide", "us", "x", 1, 2, "UTC", DateOnly.FromDateTime(DateTime.UtcNow),
        [
            new("DailySkyGuide_IntroWideSky", "WideSky", "Intro", null, null, DateTime.UtcNow, "Wide", 60, true, true, true, true, false, "IntroBackground", 1, null),
            new("DailySkyGuide_Target", "ObjectFocus", "Target", "mars", "Mars", DateTime.UtcNow, "Focus", 35, true, true, true, false, true, "TargetHighlight", 2, null)
        ], []);
}
