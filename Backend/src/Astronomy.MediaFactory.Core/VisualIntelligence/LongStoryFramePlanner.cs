using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum LongStoryFramePlatform { YouTubeLong, FacebookLong }

public sealed record LongStoryFramePlan
{
    public string Version { get; init; } = LongStoryFramePlanner.Version;
    public required string PlanId { get; init; }
    public required string StoryId { get; init; }
    public required string TimelineId { get; init; }
    public required int FrameCount { get; init; }
    public string AspectRatio { get; init; } = LongStoryFramePlanner.LandscapeAspectRatio;
    public required double TargetDurationSeconds { get; init; }
    public required IReadOnlyList<NarrativeBeat> NarrativeBeats { get; init; }
    public required IReadOnlyList<LongStoryFrameDefinition> FrameDefinitions { get; init; }
    public required LongStoryFramePlatform Platform { get; init; }
    public required double Confidence { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public sealed record LongStoryFrameDefinition
{
    public required int FrameNumber { get; init; }
    public required NarrativeBeatRole BeatRole { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string ViewerEmotion { get; init; }
    public required double TargetDuration { get; init; }
    public required string VisualPriority { get; init; }
    public required string NarrationPriority { get; init; }
    public required string RecommendedComposition { get; init; }
    public required string RecommendedSafeAreas { get; init; }
    public required string RecommendedTextDensity { get; init; }
    public required string RecommendedVisualTreatment { get; init; }
}

public sealed record LongStoryFrameReview
{
    public required string PlanId { get; init; }
    public required string TimelineId { get; init; }
    public required string AspectRatio { get; init; }
    public required int FrameCount { get; init; }
    public required IReadOnlyList<string> BeatOrder { get; init; }
    public required IReadOnlyDictionary<string, double> DurationAllocation { get; init; }
    public required IReadOnlyList<string> CompositionChecks { get; init; }
    public required IReadOnlyList<string> Recommendations { get; init; }
}

public sealed record LongStoryFrameArtifactManifest
{
    public required string PlanId { get; init; }
    public required string StoryId { get; init; }
    public required string TimelineId { get; init; }
    public required string ArtifactRoot { get; init; }
    public required IReadOnlyList<string> Directories { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required IReadOnlyList<string> ComparisonArtifacts { get; init; }
    public required IReadOnlyDictionary<string, string> Artifacts { get; init; }
    public required bool ImagesGenerated { get; init; }
    public required string RenderingStatus { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public sealed record LongStoryFrameCompositionModel
{
    public required string PlanId { get; init; }
    public required string TimelineId { get; init; }
    public required string AspectRatio { get; init; }
    public required string CompositionPhilosophy { get; init; }
    public required IReadOnlyList<string> SafeAreaRules { get; init; }
    public required IReadOnlyList<string> ProductionConstraints { get; init; }
    public required IReadOnlyDictionary<int, string> FrameCompositions { get; init; }
}


public sealed record LongStoryFrameComparisonReport
{
    public required int ExpectedFrameCount { get; init; }
    public required int GeneratedFrameCount { get; init; }
    public int FrameCount => ExpectedFrameCount;
    public int GeneratedV4FrameCount => GeneratedFrameCount;
    public bool ProductionUnchanged => ProductionSceneAssetsUnchanged;
    public required string AspectRatio { get; init; }
    public required string Provider { get; init; }
    public required bool ProductionSceneAssetsUnchanged { get; init; }
    public string Recommendation { get; init; } = "ManualReviewRequired";
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> FailedFrames { get; init; }
}
public interface ILongStoryFramePlanner
{
    LongStoryFramePlan Plan(NarrativeTimeline timeline, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong);
    Task<(LongStoryFramePlan Plan, LongStoryFrameReview Review, LongStoryFrameArtifactManifest Manifest)> WriteArtifactsAsync(NarrativeTimeline timeline, string outputFolder, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong, CancellationToken cancellationToken = default);
    Task<LongStoryFrameComparisonReport?> GenerateV4ComparisonAsync(NarrativeTimeline timeline, string outputFolder, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong, CancellationToken cancellationToken = default);
}

public sealed class LongStoryFramePlanner : ILongStoryFramePlanner
{
    private readonly VisualIntelligenceOptions options;
    private readonly IAICinematicImageGenerator? imageGenerator;
    private readonly ILogger<LongStoryFramePlanner> logger;

    public LongStoryFramePlanner(IOptions<VisualIntelligenceOptions>? options = null, IAICinematicImageGenerator? imageGenerator = null, ILogger<LongStoryFramePlanner>? logger = null)
    {
        this.options = options?.Value ?? new VisualIntelligenceOptions();
        this.imageGenerator = imageGenerator;
        this.logger = logger ?? NullLogger<LongStoryFramePlanner>.Instance;
    }

    public const string Version = "4.7H";
    public const string LandscapeAspectRatio = "16:9";
    internal const string PromptProvider = "AzureOpenAIImage";

    private static readonly NarrativeBeatRole[] RequiredLongBeatOrder =
    [
        NarrativeBeatRole.Hook,
        NarrativeBeatRole.Recognition,
        NarrativeBeatRole.ExplanationA,
        NarrativeBeatRole.ExplanationB,
        NarrativeBeatRole.ObservationA,
        NarrativeBeatRole.ObservationB,
        NarrativeBeatRole.InterestingFact,
        NarrativeBeatRole.Memory,
        NarrativeBeatRole.CallToAction
    ];

    public LongStoryFramePlan Plan(NarrativeTimeline timeline, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong)
    {
        if (timeline.TimelineType != NarrativeTimelineType.LongDocumentary)
            throw new ArgumentException("Long Story Frame plans require a long-documentary NarrativeTimeline.", nameof(timeline));

        var beats = RequiredLongBeatOrder.Select(role => timeline.Beats.First(beat => beat.BeatRole == role)).ToArray();
        var frames = beats.Select((beat, index) => BuildFrameDefinition(beat, index + 1)).ToArray();
        return new LongStoryFramePlan
        {
            PlanId = $"long_story_frames_{timeline.TimelineId}".ToLowerInvariant(),
            StoryId = timeline.StoryId,
            TimelineId = timeline.TimelineId,
            FrameCount = frames.Length,
            TargetDurationSeconds = timeline.TargetDuration,
            NarrativeBeats = beats,
            FrameDefinitions = frames,
            Platform = platform,
            Confidence = Math.Clamp(timeline.Confidence, 0, 1),
            Versions = new Dictionary<string, string>
            {
                ["longStoryFrames"] = Version,
                ["narrativeComposition"] = timeline.Version
            }
        };
    }

    public async Task<(LongStoryFramePlan Plan, LongStoryFrameReview Review, LongStoryFrameArtifactManifest Manifest)> WriteArtifactsAsync(NarrativeTimeline timeline, string outputFolder, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong, CancellationToken cancellationToken = default)
    {
        var plan = Plan(timeline, platform);
        var root = Path.Combine(outputFolder, "long-story-frames");
        var diagnostics = Path.Combine(root, "diagnostics");
        var framePrompts = Path.Combine(diagnostics, "frame-prompts");
        var comparison = Path.Combine(root, "comparison");
        Directory.CreateDirectory(diagnostics);
        Directory.CreateDirectory(framePrompts);
        Directory.CreateDirectory(comparison);

        var review = BuildReview(plan);
        var compositionModel = BuildCompositionModel(plan);
        var promptPackages = BuildPromptPackages(plan);
        var visualQualityFrameworkReview = VisualQualityFramework.Astronomy().CreateReview("Story Frames");
        var visualPromptPolicyReview = VisualPromptPolicyComposer.CreateReview(VisualPromptProduct.LongStoryFrame);
        var promptReview = BuildPromptReview(plan, promptPackages);
        var visualReview = BuildVisualReview(plan);
        var manifest = new LongStoryFrameArtifactManifest
        {
            PlanId = plan.PlanId,
            StoryId = plan.StoryId,
            TimelineId = plan.TimelineId,
            ArtifactRoot = root,
            Directories = ["diagnostics/", "diagnostics/frame-prompts/", "comparison/"],
            Diagnostics = ["diagnostics/LongStoryFramePlan.json", "diagnostics/LongStoryFrameReview.json", "diagnostics/LongStoryFramePromptReview.json", "diagnostics/LongStoryFrameVisualReview.json", "diagnostics/FrameGenerationDiagnostics.json", "diagnostics/VisualPromptDiagnostics.json", "diagnostics/VisualQualityFrameworkReview.json", "diagnostics/VisualPromptPolicyReview.json"],
            ComparisonArtifacts = ["comparison/"],
            Artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["StoryFramePlan"] = "story-frame-plan.json",
                ["CompositionModel"] = "composition-model.json",
                ["FrameReview"] = "diagnostics/LongStoryFrameReview.json",
                ["FramePromptReview"] = "diagnostics/LongStoryFramePromptReview.json",
                ["VisualReview"] = "diagnostics/LongStoryFrameVisualReview.json",
                ["FramePromptPackages"] = "diagnostics/frame-prompts/",
                ["FrameGenerationDiagnostics"] = "diagnostics/FrameGenerationDiagnostics.json",
                ["VisualPromptDiagnostics"] = "diagnostics/VisualPromptDiagnostics.json",
                ["VisualQualityFrameworkReview"] = "diagnostics/VisualQualityFrameworkReview.json",
                ["VisualPromptPolicyReview"] = "diagnostics/VisualPromptPolicyReview.json",
                ["ComparisonArtifacts"] = "comparison/"
            },
            ImagesGenerated = false,
            RenderingStatus = "Foundation only; native 16:9 frame generation planned, no production rendering replacement active.",
            Versions = plan.Versions
        };

        var options = VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "LongStoryFramePlan.json"), JsonSerializer.Serialize(plan, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "LongStoryFrameReview.json"), JsonSerializer.Serialize(review, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "LongStoryFramePromptReview.json"), JsonSerializer.Serialize(promptReview, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "LongStoryFrameVisualReview.json"), JsonSerializer.Serialize(visualReview, options), cancellationToken);
        foreach (var package in promptPackages)
            await File.WriteAllTextAsync(Path.Combine(framePrompts, PromptFileName(package.FrameNumber, package.BeatRole)), JsonSerializer.Serialize(package, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "FrameGenerationDiagnostics.json"), JsonSerializer.Serialize(CreateFrameGenerationDiagnostics(plan), options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "VisualPromptDiagnostics.json"), JsonSerializer.Serialize(CreateVisualPromptDiagnostics(plan), options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "VisualQualityFrameworkReview.json"), JsonSerializer.Serialize(visualQualityFrameworkReview, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "VisualPromptPolicyReview.json"), JsonSerializer.Serialize(visualPromptPolicyReview, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "story-frame-plan.json"), JsonSerializer.Serialize(plan, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "composition-model.json"), JsonSerializer.Serialize(compositionModel, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "LongStoryFrameArtifactManifest.json"), JsonSerializer.Serialize(manifest, options), cancellationToken);
        await GenerateV4ComparisonAsync(timeline, outputFolder, platform, cancellationToken).ConfigureAwait(false);
        return (plan, review, manifest);
    }


    public async Task<LongStoryFrameComparisonReport?> GenerateV4ComparisonAsync(NarrativeTimeline timeline, string outputFolder, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong, CancellationToken cancellationToken = default)
    {
        if (!options.UseStoryFrameV4Comparison) return null;

        var plan = Plan(timeline, platform);
        var root = Path.Combine(outputFolder, "long-story-frames");
        var diagnostics = Path.Combine(root, "diagnostics");
        var comparison = Path.Combine(root, "comparison");
        Directory.CreateDirectory(diagnostics);
        Directory.CreateDirectory(comparison);

        var visualReview = BuildVisualReview(plan);
        var packages = BuildPromptPackages(plan);
        var result = await new StoryFrameGenerator().GenerateLongAsync(plan, packages, outputFolder, imageGenerator, cancellationToken).ConfigureAwait(false);

        var report = new LongStoryFrameComparisonReport
        {
            ExpectedFrameCount = result.ExpectedFrameCount,
            GeneratedFrameCount = result.GeneratedFrameCount,
            AspectRatio = result.AspectRatio,
            Provider = result.Provider,
            ProductionSceneAssetsUnchanged = result.ProductionSceneAssetsUnchanged,
            Warnings = result.Warnings,
            FailedFrames = result.FailedFrames
        };
        var jsonOptions = VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "LongStoryFrameVisualReview.json"), JsonSerializer.Serialize(visualReview, jsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(comparison, "LongStoryFrameComparison.json"), JsonSerializer.Serialize(report, jsonOptions), cancellationToken).ConfigureAwait(false);
        var manifest = new LongStoryFrameArtifactManifest
        {
            PlanId = plan.PlanId,
            StoryId = plan.StoryId,
            TimelineId = plan.TimelineId,
            ArtifactRoot = root,
            Directories = ["diagnostics/", "comparison/"],
            Diagnostics = ["diagnostics/LongStoryFrameVisualReview.json", "diagnostics/StoryFrameGeneratorDiagnostics.json"],
            ComparisonArtifacts = ["comparison/LongStoryFrameComparison.json"],
            Artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["VisualReview"] = "diagnostics/LongStoryFrameVisualReview.json",
                ["ComparisonReport"] = "comparison/LongStoryFrameComparison.json",
                ["FrameImages"] = "./"
            },
            ImagesGenerated = report.GeneratedFrameCount > 0,
            RenderingStatus = "V4 comparison images only; production scene-assets-v3 and video rendering inputs unchanged.",
            Versions = plan.Versions
        };
        await File.WriteAllTextAsync(Path.Combine(root, "LongStoryFrameArtifactManifest.json"), JsonSerializer.Serialize(manifest, jsonOptions), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("V4 long story-frame comparison completed; production scene assets unchanged.");
        return report;
    }

    private static StoryFrameVisualReview BuildVisualReview(LongStoryFramePlan plan)
    {
        var frames = plan.FrameDefinitions.Select(frame => new StoryFrameVisualReviewFrame
        {
            FrameNumber = frame.FrameNumber,
            BeatRole = frame.BeatRole,
            ViewerQuestion = frame.ViewerQuestion,
            ViewerEmotion = frame.ViewerEmotion,
            ExpectedVisualIntent = frame.RecommendedVisualTreatment,
            GeneratedFramePath = $"frame{frame.FrameNumber:00}-{Slug(frame.BeatRole)}.png",
            VisualContinuityNotes = "Advisory review placeholder: validate landscape documentary continuity, beat-to-beat subject continuity, lighting continuity, and narrative handoff manually; no CV/image analysis has been run.",
            PlatformCompositionNotes = $"Advisory review placeholder: validate native 16:9 landscape safe areas, overlay clearance, and platform-native composition manually. Planned composition: {frame.RecommendedComposition}",
            Risks = ["No image analysis/CV has been run.", "Generated comparison frame may be missing or may have failed non-blocking.", "Manual astronomy and typography review is required before production use."],
            Recommendation = "ManualReviewRequired"
        }).ToArray();

        var versions = new Dictionary<string, string>(plan.Versions) { ["longStoryFrameVisualReview"] = Version };
        return new StoryFrameVisualReview
        {
            ReviewId = $"{plan.PlanId}_visual_review",
            PlanId = plan.PlanId,
            FramePlanId = plan.PlanId,
            StoryId = plan.StoryId,
            AspectRatio = plan.AspectRatio,
            Platform = plan.Platform.ToString(),
            FrameCount = plan.FrameCount,
            ReviewedFrames = frames,
            StoryContinuityScore = 0,
            PlatformNativeScore = 0,
            DocumentaryScore = 0,
            EducationalProgressionScore = 0,
            VisualConsistencyScore = 0,
            TypographySafetyScore = 0,
            AstronomyAccuracyScore = 0,
            OverallScore = 0,
            Recommendation = "Native landscape story-frame visual review is advisory only; manual review required before production adoption.",
            Warnings = ["Diagnostics only; scores are unset because no image analysis/CV is available yet.", "Production scene assets, video rendering, and Azure routing are unchanged.", "Comparison image generation failures remain non-blocking."],
            CriticalIssues = [],
            Versions = versions
        };
    }

    private static object CreateFrameGenerationDiagnostics(LongStoryFramePlan plan) => new
    {
        plan.PlanId,
        plan.TimelineId,
        ImagesGenerated = false,
        AzureCallsMade = false,
        ProductionSceneRenderingReplaced = false,
        Status = "Artifact alignment only; no native story frame image generation has run."
    };

    private static object CreateVisualPromptDiagnostics(LongStoryFramePlan plan) => new
    {
        plan.PlanId,
        plan.TimelineId,
        PromptReplacementApplied = false,
        VisualPromptsGenerated = true,
        Status = "Story frame prompt packages generated for future comparison only; scene rendering prompts remain unchanged."
    };

    internal static IReadOnlyList<StoryFramePromptPackage> BuildPromptPackages(LongStoryFramePlan plan) =>
        plan.FrameDefinitions.Select(frame => new StoryFramePromptPackage
        {
            PackageId = $"{plan.PlanId}_frame{frame.FrameNumber:00}_{frame.BeatRole}_prompt".ToLowerInvariant(),
            PlanId = plan.PlanId,
            FramePlanId = $"{plan.PlanId}_frame{frame.FrameNumber:00}",
            FrameNumber = frame.FrameNumber,
            BeatRole = frame.BeatRole,
            AspectRatio = plan.AspectRatio,
            Platform = plan.Platform.ToString(),
            Provider = PromptProvider,
            VisualTreatment = frame.RecommendedVisualTreatment,
            SafeAreaInstructions = "Reserve deterministic overlay safe space in the lower third and outer edge margins; keep astronomy subjects in the central documentary field.",
            TypographyInstructions = "Do not generate embedded text, captions, labels, letters, numbers, logos, UI, watermarks, or title cards inside the image.",
            PositivePrompt = $"{VisualPromptPolicyComposer.Compose(VisualPromptProduct.LongStoryFrame).PositiveGuidance} {frame.RecommendedVisualTreatment} Beat intent: {frame.VisualPriority}. Preserve astronomy accuracy, realistic apparent scale, natural sky lighting, and scientifically plausible object placement. Compose with deterministic overlay safe space in the lower third and edge margins. No generated embedded text.",
            NegativePrompt = VisualPromptPolicyComposer.Compose(VisualPromptProduct.LongStoryFrame).NegativeGuidance + ", text, words, letters, numbers, captions, labels, logo, watermark, title card, UI chrome, inaccurate astronomy, impossible object scale, distorted constellations",
            Diagnostics = new Dictionary<string, object>
            {
                ["azureCallsMade"] = false,
                ["imageGenerationRequested"] = false,
                ["scenePromptReplacementApplied"] = false,
                ["noCroppingLanguage"] = true,
                ["embeddedTextProhibited"] = true,
                ["visualQualityFrameworkVersion"] = VisualQualityFramework.Version,
                ["visualQualityFrameworkLoaded"] = true
            },
            Versions = new Dictionary<string, string>(plan.Versions) { ["storyFramePromptPackages"] = Version, ["visualQualityFramework"] = VisualQualityFramework.Version }
        }).ToArray();

    private static StoryFramePromptReview BuildPromptReview(LongStoryFramePlan plan, IReadOnlyList<StoryFramePromptPackage> packages) => new()
    {
        PlanId = plan.PlanId,
        PromptCount = packages.Count,
        AspectRatio = plan.AspectRatio,
        Provider = PromptProvider,
        NoCroppingConfirmed = packages.All(package => !ContainsCroppingLanguage(package.PositivePrompt) && !ContainsCroppingLanguage(package.NegativePrompt)),
        EmbeddedTextProhibited = packages.All(package => package.TypographyInstructions.Contains("Do not generate embedded text", StringComparison.OrdinalIgnoreCase) && package.PositivePrompt.Contains("No generated embedded text", StringComparison.OrdinalIgnoreCase)),
        BeatCoverage = packages.Select(package => package.BeatRole.ToString()).ToArray(),
        Warnings = []
    };

    private static bool ContainsCroppingLanguage(string value) => value.Contains("crop", StringComparison.OrdinalIgnoreCase);

    private static string PromptFileName(int frameNumber, NarrativeBeatRole beatRole) => $"frame{frameNumber:00}-{Slug(beatRole)}-prompt.json";

    internal static string Slug(NarrativeBeatRole beatRole) => beatRole switch
    {
        NarrativeBeatRole.CallToAction => "call-to-action",
        NarrativeBeatRole.ExplanationA => "explanation-a",
        NarrativeBeatRole.ExplanationB => "explanation-b",
        NarrativeBeatRole.ObservationA => "observation-a",
        NarrativeBeatRole.ObservationB => "observation-b",
        NarrativeBeatRole.InterestingFact => "interesting-fact",
        _ => beatRole.ToString().ToLowerInvariant()
    };

    private static LongStoryFrameCompositionModel BuildCompositionModel(LongStoryFramePlan plan) => new()
    {
        PlanId = plan.PlanId,
        TimelineId = plan.TimelineId,
        AspectRatio = plan.AspectRatio,
        CompositionPhilosophy = "Native 16:9 long-form documentary frames aligned with Hero and Gallery artifact diagnostics without replacing scene rendering.",
        SafeAreaRules = ["Keep primary astronomy subject inside the central 80% width and 78% height; reserve lower-third and edge margins for platform UI and captions."],
        ProductionConstraints = ["Artifact alignment only.", "No image generation changes.", "No Azure changes.", "No prompt replacement.", "No production scene rendering replacement."],
        FrameCompositions = plan.FrameDefinitions.ToDictionary(frame => frame.FrameNumber, frame => frame.RecommendedComposition)
    };

    private static LongStoryFrameDefinition BuildFrameDefinition(NarrativeBeat beat, int frameNumber) => new()
    {
        FrameNumber = frameNumber,
        BeatRole = beat.BeatRole,
        ViewerQuestion = beat.ViewerQuestion,
        ViewerEmotion = beat.ViewerEmotion,
        TargetDuration = beat.TargetDuration,
        VisualPriority = beat.VisualPriority,
        NarrationPriority = beat.NarrationPriority,
        RecommendedComposition = $"Native 16:9 documentary landscape composition. {beat.RecommendedComposition}. Do not crop portrait or square assets; do not reuse short-frame composition.",
        RecommendedSafeAreas = "Keep primary astronomy subject inside the central 80% width and 78% height; reserve lower-third and edge margins for platform UI and captions.",
        RecommendedTextDensity = beat.BeatRole is NarrativeBeatRole.Hook or NarrativeBeatRole.CallToAction ? "Low: one concise documentary line." : "Minimal: prefer narration over on-frame text.",
        RecommendedVisualTreatment = TreatmentFor(beat.BeatRole)
    };

    private static string TreatmentFor(NarrativeBeatRole role) => role switch
    {
        NarrativeBeatRole.Hook => "Wide cinematic establishing frame with strong subject recognition and documentary intrigue.",
        NarrativeBeatRole.Recognition => "Landscape identification frame with clear subject separation and viewer orientation.",
        NarrativeBeatRole.ExplanationA or NarrativeBeatRole.ExplanationB => "Educational landscape visual relationship frame; use space, labels sparingly, and depth cues.",
        NarrativeBeatRole.ObservationA or NarrativeBeatRole.ObservationB => "Practical observing frame with horizon/sky context and realistic night-sky scale.",
        NarrativeBeatRole.InterestingFact => "Memorable documentary insert frame that highlights the surprising detail without visual exaggeration.",
        NarrativeBeatRole.Memory => "Reflective wide frame that reinforces the takeaway with calm negative space.",
        NarrativeBeatRole.CallToAction => "Closing landscape frame with safe lower-third action space and no short-form layout reuse.",
        _ => "Native 16:9 documentary frame treatment."
    };

    private static LongStoryFrameReview BuildReview(LongStoryFramePlan plan) => new()
    {
        PlanId = plan.PlanId,
        TimelineId = plan.TimelineId,
        AspectRatio = plan.AspectRatio,
        FrameCount = plan.FrameCount,
        BeatOrder = plan.FrameDefinitions.Select(frame => frame.BeatRole.ToString()).ToArray(),
        DurationAllocation = plan.FrameDefinitions.ToDictionary(frame => frame.BeatRole.ToString(), frame => frame.TargetDuration),
        CompositionChecks = ["All frames are planned as native 16:9 landscape frames.", "No portrait or square asset crop is prescribed.", "No short-story frame composition is reused.", "Scene rendering and production routing remain unchanged."],
        Recommendations = plan.FrameDefinitions.Select(frame => $"Frame {frame.FrameNumber} {frame.BeatRole}: {frame.RecommendedVisualTreatment}").ToArray()
    };
}
