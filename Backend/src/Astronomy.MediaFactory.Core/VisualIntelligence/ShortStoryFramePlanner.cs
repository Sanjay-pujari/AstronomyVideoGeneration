using System.Text.Json;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum ShortStoryFramePlatform { YouTubeShorts, FacebookShorts, InstagramReels }

public sealed record ShortStoryFramePlan
{
    public string Version { get; init; } = ShortStoryFramePlanner.Version;
    public required string PlanId { get; init; }
    public required string StoryId { get; init; }
    public required string TimelineId { get; init; }
    public required int FrameCount { get; init; }
    public string AspectRatio { get; init; } = ShortStoryFramePlanner.PortraitAspectRatio;
    public required double TargetDurationSeconds { get; init; }
    public required IReadOnlyList<NarrativeBeat> NarrativeBeats { get; init; }
    public required IReadOnlyList<ShortStoryFrameDefinition> FrameDefinitions { get; init; }
    public required ShortStoryFramePlatform Platform { get; init; }
    public required double Confidence { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public sealed record ShortStoryFrameDefinition
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

public sealed record ShortStoryFrameReview
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

public sealed record ShortStoryFrameArtifactManifest
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

public interface IShortStoryFramePlanner
{
    ShortStoryFramePlan Plan(NarrativeTimeline timeline, ShortStoryFramePlatform platform = ShortStoryFramePlatform.YouTubeShorts);
    Task<(ShortStoryFramePlan Plan, ShortStoryFrameReview Review, ShortStoryFrameArtifactManifest Manifest)> WriteArtifactsAsync(NarrativeTimeline timeline, string outputFolder, ShortStoryFramePlatform platform = ShortStoryFramePlatform.YouTubeShorts, CancellationToken cancellationToken = default);
}

public sealed class ShortStoryFramePlanner : IShortStoryFramePlanner
{
    public const string Version = "4.7C";
    public const string PortraitAspectRatio = "9:16";

    private static readonly NarrativeBeatRole[] RequiredShortBeatOrder =
    [
        NarrativeBeatRole.Hook,
        NarrativeBeatRole.Recognition,
        NarrativeBeatRole.Explanation,
        NarrativeBeatRole.Observation,
        NarrativeBeatRole.CallToAction
    ];

    public ShortStoryFramePlan Plan(NarrativeTimeline timeline, ShortStoryFramePlatform platform = ShortStoryFramePlatform.YouTubeShorts)
    {
        if (timeline.TimelineType != NarrativeTimelineType.ShortDocumentary)
            throw new ArgumentException("Short Story Frame plans require a short-documentary NarrativeTimeline.", nameof(timeline));

        var beats = RequiredShortBeatOrder.Select(role => timeline.Beats.First(beat => beat.BeatRole == role)).ToArray();
        var frames = beats.Select((beat, index) => BuildFrameDefinition(beat, index + 1)).ToArray();
        return new ShortStoryFramePlan
        {
            PlanId = $"short_story_frames_{timeline.TimelineId}".ToLowerInvariant(),
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
                ["shortStoryFrames"] = Version,
                ["narrativeComposition"] = timeline.Version
            }
        };
    }

    public async Task<(ShortStoryFramePlan Plan, ShortStoryFrameReview Review, ShortStoryFrameArtifactManifest Manifest)> WriteArtifactsAsync(NarrativeTimeline timeline, string outputFolder, ShortStoryFramePlatform platform = ShortStoryFramePlatform.YouTubeShorts, CancellationToken cancellationToken = default)
    {
        var plan = Plan(timeline, platform);
        var root = Path.Combine(outputFolder, "short-story-frames");
        var diagnostics = Path.Combine(root, "diagnostics");
        var comparison = Path.Combine(root, "comparison");
        Directory.CreateDirectory(diagnostics);
        Directory.CreateDirectory(comparison);

        var review = BuildReview(plan);
        var manifest = new ShortStoryFrameArtifactManifest
        {
            PlanId = plan.PlanId,
            StoryId = plan.StoryId,
            TimelineId = plan.TimelineId,
            ArtifactRoot = root,
            Directories = ["diagnostics/", "comparison/"],
            Diagnostics = ["diagnostics/ShortStoryFramePlan.json", "diagnostics/ShortStoryFrameReview.json"],
            ComparisonArtifacts = [],
            ImagesGenerated = false,
            RenderingStatus = "Foundation only; native 9:16 short-form frame generation planned, no production rendering replacement active, no Azure routing changes active.",
            Versions = plan.Versions
        };

        var options = VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "ShortStoryFramePlan.json"), JsonSerializer.Serialize(plan, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(diagnostics, "ShortStoryFrameReview.json"), JsonSerializer.Serialize(review, options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "ShortStoryFrameArtifactManifest.json"), JsonSerializer.Serialize(manifest, options), cancellationToken);
        return (plan, review, manifest);
    }

    private static ShortStoryFrameDefinition BuildFrameDefinition(NarrativeBeat beat, int frameNumber) => new()
    {
        FrameNumber = frameNumber,
        BeatRole = beat.BeatRole,
        ViewerQuestion = beat.ViewerQuestion,
        ViewerEmotion = beat.ViewerEmotion,
        TargetDuration = beat.TargetDuration,
        VisualPriority = beat.VisualPriority,
        NarrationPriority = beat.NarrationPriority,
        RecommendedComposition = $"Native 9:16 vertical short-form composition for YouTube Shorts, Facebook Shorts, and Instagram Reels. {beat.RecommendedComposition}. Do not crop landscape assets; do not reuse long-frame composition. Prioritize fast comprehension and strong vertical visual hierarchy.",
        RecommendedSafeAreas = "Keep primary astronomy subject in the central vertical column; reserve top 12%, bottom 18%, and right-edge UI margin for platform chrome, captions, and engagement controls.",
        RecommendedTextDensity = beat.BeatRole is NarrativeBeatRole.Hook or NarrativeBeatRole.CallToAction ? "Low: one short high-contrast phrase with immediate readability." : "Very low: narration-led, only essential labels if they improve fast comprehension.",
        RecommendedVisualTreatment = TreatmentFor(beat.BeatRole)
    };

    private static string TreatmentFor(NarrativeBeatRole role) => role switch
    {
        NarrativeBeatRole.Hook => "Vertical hook frame with instant subject recognition, bold top-to-middle hierarchy, and motion-implied urgency.",
        NarrativeBeatRole.Recognition => "Portrait identification frame with subject isolated in the central column and minimal competing detail.",
        NarrativeBeatRole.Explanation => "Compact educational vertical relationship frame; show the astronomy relationship in one glance with sparse labels.",
        NarrativeBeatRole.Observation => "Fast practical observing frame with horizon-to-sky vertical guidance and clear viewer action cue.",
        NarrativeBeatRole.CallToAction => "Closing vertical action frame with safe text space and strong save/follow/observe cue.",
        _ => "Native 9:16 short-form frame treatment."
    };

    private static ShortStoryFrameReview BuildReview(ShortStoryFramePlan plan) => new()
    {
        PlanId = plan.PlanId,
        TimelineId = plan.TimelineId,
        AspectRatio = plan.AspectRatio,
        FrameCount = plan.FrameCount,
        BeatOrder = plan.FrameDefinitions.Select(frame => frame.BeatRole.ToString()).ToArray(),
        DurationAllocation = plan.FrameDefinitions.ToDictionary(frame => frame.BeatRole.ToString(), frame => frame.TargetDuration),
        CompositionChecks = ["All frames are planned as native 9:16 vertical short-form frames.", "Landscape assets must not be cropped into portrait frames.", "Long-frame composition is not reused.", "Scene rendering, prompt routing, Azure routing, and production routing remain unchanged."],
        Recommendations = plan.FrameDefinitions.Select(frame => $"Frame {frame.FrameNumber} {frame.BeatRole}: {frame.RecommendedVisualTreatment}").ToArray()
    };
}
