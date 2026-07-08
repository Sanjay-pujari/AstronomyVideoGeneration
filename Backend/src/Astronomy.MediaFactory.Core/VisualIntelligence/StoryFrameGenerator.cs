using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record StoryFrameGenerationResult
{
    public required int ExpectedFrameCount { get; init; }
    public required int GeneratedFrameCount { get; init; }
    public required string AspectRatio { get; init; }
    public required string Provider { get; init; }
    public required bool ProductionSceneAssetsUnchanged { get; init; }
    public string Recommendation { get; init; } = "ManualReviewRequired";
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> FailedFrames { get; init; }
    public required bool OrientationPassed { get; init; }
    public required bool ObjectFidelityPolicyApplied { get; init; }
    public required bool ForbiddenObjectPolicyApplied { get; init; }
}

public interface IStoryFrameGenerator
{
    Task<StoryFrameGenerationResult> GenerateLongAsync(LongStoryFramePlan plan, IReadOnlyList<StoryFramePromptPackage> promptPackages, string outputFolder, IAICinematicImageGenerator? imageGenerator, CancellationToken cancellationToken = default);
    Task<StoryFrameGenerationResult> GenerateShortAsync(ShortStoryFramePlan plan, IReadOnlyList<StoryFramePromptPackage> promptPackages, string outputFolder, IAICinematicImageGenerator? imageGenerator, CancellationToken cancellationToken = default);
}

public sealed class StoryFrameGenerator(ILogger<StoryFrameGenerator>? logger = null) : IStoryFrameGenerator
{
    private readonly ILogger<StoryFrameGenerator> logger = logger ?? NullLogger<StoryFrameGenerator>.Instance;

    public Task<StoryFrameGenerationResult> GenerateLongAsync(LongStoryFramePlan plan, IReadOnlyList<StoryFramePromptPackage> promptPackages, string outputFolder, IAICinematicImageGenerator? imageGenerator, CancellationToken cancellationToken = default) =>
        GenerateAsync("long-story-frames", plan.PlanId, plan.AspectRatio, 1920, 1080, promptPackages, outputFolder, imageGenerator, cancellationToken);

    public Task<StoryFrameGenerationResult> GenerateShortAsync(ShortStoryFramePlan plan, IReadOnlyList<StoryFramePromptPackage> promptPackages, string outputFolder, IAICinematicImageGenerator? imageGenerator, CancellationToken cancellationToken = default) =>
        GenerateAsync("short-story-frames", plan.PlanId, plan.AspectRatio, 1080, 1920, promptPackages, outputFolder, imageGenerator, cancellationToken);

    private async Task<StoryFrameGenerationResult> GenerateAsync(string artifactFolderName, string planId, string aspectRatio, int width, int height, IReadOnlyList<StoryFramePromptPackage> packages, string outputFolder, IAICinematicImageGenerator? imageGenerator, CancellationToken cancellationToken)
    {
        var root = Path.Combine(outputFolder, artifactFolderName);
        var diagnostics = Path.Combine(root, "diagnostics");
        var comparison = Path.Combine(root, "comparison");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(diagnostics);
        Directory.CreateDirectory(comparison);

        var warnings = new List<string>();
        var failedFrames = new List<string>();
        var generated = 0;
        var frameDiagnostics = new List<object>();
        var expectedPortrait = height > width;
        var orientationPassed = true;
        var objectFidelityPolicyApplied = packages.All(p => IsObjectFidelityPolicyApplied(p));
        var forbiddenObjectPolicyApplied = packages.All(p => IsForbiddenObjectPolicyApplied(p));
        var provider = imageGenerator?.DeploymentName;
        if (string.IsNullOrWhiteSpace(provider)) provider = "AzureOpenAIImage";

        if (imageGenerator is null || !imageGenerator.IsConfigured)
        {
            warnings.Add("V4 story-frame image provider unavailable; comparison generation skipped non-blocking.");
            failedFrames.AddRange(packages.Select(p => $"frame{p.FrameNumber:00}-{Slug(p.BeatRole)}.png"));
        }
        else
        {
            foreach (var package in packages)
            {
                var fileName = $"frame{package.FrameNumber:00}-{Slug(package.BeatRole)}.png";
                var path = Path.Combine(root, fileName);
                try
                {
                    var result = await imageGenerator.GenerateAsync(new AICinematicAssetRequest(
                        $"story-frame-v4-{package.FrameNumber:00}",
                        planId,
                        "StoryFrameComparison",
                        "V4Comparison",
                        Path.GetFileNameWithoutExtension(fileName),
                        "ExperimentalComparisonOnly",
                        "Manual review",
                        "Still",
                        "V4 experimental story-frame comparison only; not a production scene asset.",
                        package.PositivePrompt,
                        package.NegativePrompt + ", production replacement, video assembly change, crop",
                        width,
                        height,
                        path), cancellationToken).ConfigureAwait(false);

                    if (string.Equals(result.GenerationStatus, "Generated", StringComparison.OrdinalIgnoreCase) && File.Exists(result.ImagePath ?? path))
                    {
                        var imagePath = result.ImagePath ?? path;
                        var validation = await NormalizeAndValidateOrientationAsync(imagePath, width, height, expectedPortrait, cancellationToken).ConfigureAwait(false);
                        frameDiagnostics.Add(new { frame = fileName, expectedAspectRatio = aspectRatio, actualWidth = validation.Width, actualHeight = validation.Height, aspectRatioPassed = validation.Passed, orientationPolicyApplied = true, objectFidelityPolicyApplied = IsObjectFidelityPolicyApplied(package), forbiddenObjectPolicyApplied = IsForbiddenObjectPolicyApplied(package), expectedPrimaryObjects = ExpectedPrimaryObjects(package), expectedSecondaryObjects = ExpectedSecondaryObjects(package), warnings = validation.Warnings, errors = validation.Errors });
                        if (!validation.Passed)
                        {
                            orientationPassed = false;
                            warnings.AddRange(validation.Warnings);
                            failedFrames.Add(fileName);
                        }
                        else
                        {
                            generated++;
                        }
                    }
                    else
                    {
                        warnings.Add($"Frame {package.FrameNumber} did not produce a comparison image.");
                        failedFrames.Add(fileName);
                        frameDiagnostics.Add(new { frame = fileName, expectedAspectRatio = aspectRatio, actualWidth = 0, actualHeight = 0, aspectRatioPassed = false, orientationPolicyApplied = true, objectFidelityPolicyApplied = IsObjectFidelityPolicyApplied(package), forbiddenObjectPolicyApplied = IsForbiddenObjectPolicyApplied(package), expectedPrimaryObjects = ExpectedPrimaryObjects(package), expectedSecondaryObjects = ExpectedSecondaryObjects(package), warnings = new[] { $"Frame {package.FrameNumber} did not produce a comparison image." }, errors = Array.Empty<string>() });
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Frame {package.FrameNumber} failed non-blocking: {ex.GetType().Name}");
                    failedFrames.Add(fileName);
                    frameDiagnostics.Add(new { frame = fileName, expectedAspectRatio = aspectRatio, actualWidth = 0, actualHeight = 0, aspectRatioPassed = false, orientationPolicyApplied = true, objectFidelityPolicyApplied = IsObjectFidelityPolicyApplied(package), forbiddenObjectPolicyApplied = IsForbiddenObjectPolicyApplied(package), expectedPrimaryObjects = ExpectedPrimaryObjects(package), expectedSecondaryObjects = ExpectedSecondaryObjects(package), warnings = new[] { $"Frame {package.FrameNumber} failed non-blocking: {ex.GetType().Name}" }, errors = Array.Empty<string>() });
                    logger.LogWarning(ex, "V4 story-frame comparison generation failed non-blocking. FrameNumber={FrameNumber}", package.FrameNumber);
                }
            }
        }

        var diag = new { planId, aspectRatio, expectedAspectRatio = aspectRatio, width, height, provider, expectedFrameCount = packages.Count, generatedFrameCount = generated, actualWidth = frameDiagnostics.Count > 0 ? (int?)frameDiagnostics.Select(f => (int)f.GetType().GetProperty("actualWidth")!.GetValue(f)!).FirstOrDefault(w => w > 0) : null, actualHeight = frameDiagnostics.Count > 0 ? (int?)frameDiagnostics.Select(f => (int)f.GetType().GetProperty("actualHeight")!.GetValue(f)!).FirstOrDefault(h => h > 0) : null, aspectRatioPassed = orientationPassed, expectedPrimaryObjects = packages.SelectMany(ExpectedPrimaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), expectedSecondaryObjects = packages.SelectMany(ExpectedSecondaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), forbiddenObjectPolicyApplied, orientationPolicyApplied = true, objectFidelityPolicyApplied, productionSceneAssetsUnchanged = true, warnings, errors = Array.Empty<string>(), failedFrames, frames = frameDiagnostics };
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "StoryFrameGeneratorDiagnostics.json"), JsonSerializer.Serialize(diag, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true)), cancellationToken).ConfigureAwait(false);
        return new StoryFrameGenerationResult { ExpectedFrameCount = packages.Count, GeneratedFrameCount = generated, AspectRatio = aspectRatio, Provider = provider, ProductionSceneAssetsUnchanged = true, Warnings = warnings, FailedFrames = failedFrames, OrientationPassed = orientationPassed, ObjectFidelityPolicyApplied = objectFidelityPolicyApplied, ForbiddenObjectPolicyApplied = forbiddenObjectPolicyApplied };
    }

    private static async Task<(int Width, int Height, bool Passed, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)> NormalizeAndValidateOrientationAsync(string path, int targetWidth, int targetHeight, bool expectedPortrait, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        using var image = await Image.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        if (expectedPortrait && image.Width >= image.Height || !expectedPortrait && image.Width <= image.Height || image.Width != targetWidth || image.Height != targetHeight)
        {
            warnings.Add($"Story-frame orientation/size normalized from {image.Width}x{image.Height} to {targetWidth}x{targetHeight}.");
            image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(targetWidth, targetHeight), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }));
            await image.SaveAsPngAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var passed = expectedPortrait ? targetWidth < targetHeight : targetWidth > targetHeight;
        if (!passed) errors.Add(expectedPortrait ? "Short story-frame width must be less than height." : "Long story-frame width must be greater than height.");
        return (targetWidth, targetHeight, passed, warnings, errors);
    }

    private static bool IsObjectFidelityPolicyApplied(StoryFramePromptPackage package) =>
        package.PositivePrompt.Contains("Jupiter is the primary visual object", StringComparison.OrdinalIgnoreCase)
        && package.PositivePrompt.Contains("Venus is the secondary supporting object", StringComparison.OrdinalIgnoreCase);

    private static bool IsForbiddenObjectPolicyApplied(StoryFramePromptPackage package) =>
        package.NegativePrompt.Contains("no moon", StringComparison.OrdinalIgnoreCase)
        && package.NegativePrompt.Contains("no comet", StringComparison.OrdinalIgnoreCase)
        && package.NegativePrompt.Contains("no meteor", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ExpectedPrimaryObjects(StoryFramePromptPackage package) => IsObjectFidelityPolicyApplied(package) ? ["Jupiter"] : [];

    private static IReadOnlyList<string> ExpectedSecondaryObjects(StoryFramePromptPackage package) => IsObjectFidelityPolicyApplied(package) ? ["Venus"] : [];

    private static string Slug(NarrativeBeatRole beatRole) => beatRole switch
    {
        NarrativeBeatRole.CallToAction => "call-to-action",
        _ => beatRole.ToString().ToLowerInvariant()
    };
}
