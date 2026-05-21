using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumImageCaptureExecutorTests
{
    [Fact]
    public async Task DryRun_UsesCaptureDirectory_AndDoesNotCreateDirectory()
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), $"stellarium-captures-{Guid.NewGuid():N}");
        var options = Options.Create(new StellariumOptions { CaptureDirectory = captureRoot, Enabled = true });
        var sut = new StellariumImageCaptureExecutor(options, NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();

        var result = await sut.CaptureAsync(BuildPlan(planId), new StellariumCaptureExecutionRequest(planId, DryRun: true), CancellationToken.None);

        var expectedFolder = Path.Combine(captureRoot, "content-plans", planId.ToString(), "stellarium-scenes");
        Assert.Equal(expectedFolder, result.OutputFolder);
        Assert.Contains("DryRun enabled. No images were captured.", result.Warnings);
        Assert.DoesNotContain("Stellarium:CaptureDirectory is not configured; fallback output path used.", result.Warnings);
        Assert.False(Directory.Exists(expectedFolder));
        Assert.All(result.Images, image => Assert.StartsWith(expectedFolder, image.ImagePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingCaptureDirectory_UsesFallbackOutputRoot_AndReturnsWarning()
    {
        var fallbackRoot = Path.Combine(Path.GetTempPath(), $"stellarium-outputroot-{Guid.NewGuid():N}");
        var options = Options.Create(new StellariumOptions { OutputRoot = fallbackRoot, CaptureDirectory = "", Enabled = true });
        var sut = new StellariumImageCaptureExecutor(options, NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();

        var result = await sut.CaptureAsync(BuildPlan(planId), new StellariumCaptureExecutionRequest(planId, DryRun: true), CancellationToken.None);

        var expectedFolder = Path.Combine(fallbackRoot, planId.ToString(), "stellarium-scenes");
        Assert.Equal(expectedFolder, result.OutputFolder);
        Assert.Contains("Stellarium:CaptureDirectory is not configured; fallback output path used.", result.Warnings);
        Assert.False(Directory.Exists(expectedFolder));
    }

    [Fact]
    public async Task RealCapture_CreatesDirectory_WhenDryRunFalse()
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), $"stellarium-captures-{Guid.NewGuid():N}");
        var options = Options.Create(new StellariumOptions { CaptureDirectory = captureRoot, Enabled = false });
        var sut = new StellariumImageCaptureExecutor(options, NullLogger<StellariumImageCaptureExecutor>.Instance);
        var planId = Guid.NewGuid();

        var result = await sut.CaptureAsync(BuildPlan(planId), new StellariumCaptureExecutionRequest(planId, DryRun: false), CancellationToken.None);

        Assert.True(Directory.Exists(result.OutputFolder));
        Assert.Contains("Stellarium capture is disabled in configuration.", result.Warnings);
    }

    [Fact]
    public async Task DisabledConfig_ReturnsWarning()
    {
        var options = Options.Create(new StellariumOptions { CaptureDirectory = Path.Combine(Path.GetTempPath(), $"stellarium-captures-{Guid.NewGuid():N}"), Enabled = false });
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
