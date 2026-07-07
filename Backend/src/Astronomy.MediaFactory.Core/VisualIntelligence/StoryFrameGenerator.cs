using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        GenerateAsync("long-story-frames", plan.PlanId, plan.AspectRatio, 1792, 1008, promptPackages, outputFolder, imageGenerator, cancellationToken);

    public Task<StoryFrameGenerationResult> GenerateShortAsync(ShortStoryFramePlan plan, IReadOnlyList<StoryFramePromptPackage> promptPackages, string outputFolder, IAICinematicImageGenerator? imageGenerator, CancellationToken cancellationToken = default) =>
        GenerateAsync("short-story-frames", plan.PlanId, plan.AspectRatio, 1008, 1792, promptPackages, outputFolder, imageGenerator, cancellationToken);

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
                        generated++;
                    }
                    else
                    {
                        warnings.Add($"Frame {package.FrameNumber} did not produce a comparison image.");
                        failedFrames.Add(fileName);
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Frame {package.FrameNumber} failed non-blocking: {ex.GetType().Name}");
                    failedFrames.Add(fileName);
                    logger.LogWarning(ex, "V4 story-frame comparison generation failed non-blocking. FrameNumber={FrameNumber}", package.FrameNumber);
                }
            }
        }

        var diag = new { planId, aspectRatio, width, height, provider, expectedFrameCount = packages.Count, generatedFrameCount = generated, productionSceneAssetsUnchanged = true, warnings, failedFrames };
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "StoryFrameGeneratorDiagnostics.json"), JsonSerializer.Serialize(diag, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true)), cancellationToken).ConfigureAwait(false);
        return new StoryFrameGenerationResult { ExpectedFrameCount = packages.Count, GeneratedFrameCount = generated, AspectRatio = aspectRatio, Provider = provider, ProductionSceneAssetsUnchanged = true, Warnings = warnings, FailedFrames = failedFrames };
    }

    private static string Slug(NarrativeBeatRole beatRole) => beatRole switch
    {
        NarrativeBeatRole.CallToAction => "call-to-action",
        _ => beatRole.ToString().ToLowerInvariant()
    };
}
