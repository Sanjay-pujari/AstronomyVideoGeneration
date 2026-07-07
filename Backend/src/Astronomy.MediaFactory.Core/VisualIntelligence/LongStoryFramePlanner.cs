using System.Text.Json;
using Astronomy.MediaFactory.Contracts;

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
    public required bool ImagesGenerated { get; init; }
    public required string RenderingStatus { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public interface ILongStoryFramePlanner
{
    LongStoryFramePlan Plan(NarrativeTimeline timeline, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong);
    Task<(LongStoryFramePlan Plan, LongStoryFrameReview Review, LongStoryFrameArtifactManifest Manifest)> WriteArtifactsAsync(NarrativeTimeline timeline, string outputFolder, LongStoryFramePlatform platform = LongStoryFramePlatform.YouTubeLong, CancellationToken cancellationToken = default);
}

public sealed class LongStoryFramePlanner : ILongStoryFramePlanner
{
    public const string Version = "4.7B";
    public const string LandscapeAspectRatio = "16:9";

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
        var comparison = Path.Combine(root, "comparison");
        Directory.CreateDirectory(diagnostics);
        Directory.CreateDirectory(comparison);

        var review = BuildReview(plan);
        var manifest = new LongStoryFrameArtifactManifest
        {
            PlanId = plan.PlanId,
            StoryId = plan.StoryId,
            TimelineId = plan.TimelineId,
            ArtifactRoot = root,
            Directories = ["diagnostics/", "comparison/"],
            Diagnostics = ["diagnostics/LongStoryFramePlan.json", "diagnostics/LongStoryFrameReview.json"],
            ComparisonArtifacts = [],
            ImagesGenerated = false,
            RenderingStatus = "Foundation only; native 16:9 frame generation planned, no production rendering replacement active.",
            Versions = plan.Versions
        };

        var options = VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "LongStoryFramePlan.json"), JsonSerializer.Serialize(plan, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "LongStoryFrameReview.json"), JsonSerializer.Serialize(review, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "LongStoryFrameArtifactManifest.json"), JsonSerializer.Serialize(manifest, options), cancellationToken);
        return (plan, review, manifest);
    }

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
