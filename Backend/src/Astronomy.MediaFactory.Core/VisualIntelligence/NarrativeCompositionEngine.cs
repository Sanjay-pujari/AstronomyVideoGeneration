using System.Text.Json;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum NarrativeTimelineType { LongDocumentary, ShortDocumentary }
public enum NarrativeBeatRole { Hook, Recognition, ExplanationA, ExplanationB, Explanation, ObservationA, ObservationB, Observation, InterestingFact, Memory, CallToAction }

public sealed record NarrativeBeat
{
    public required string BeatId { get; init; }
    public required NarrativeBeatRole BeatRole { get; init; }
    public required string BeatGoal { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string ViewerEmotion { get; init; }
    public required double TargetDuration { get; init; }
    public required string VisualPriority { get; init; }
    public required string NarrationPriority { get; init; }
    public required string EducationalPriority { get; init; }
    public required string RecommendedComposition { get; init; }
    public IReadOnlyList<string> RecommendedPlatformVariants { get; init; } = [];
    public required double Confidence { get; init; }
}

public sealed record NarrativeTimeline
{
    public string Version { get; init; } = NarrativeCompositionEngine.Version;
    public required string TimelineId { get; init; }
    public required NarrativeTimelineType TimelineType { get; init; }
    public required string StoryId { get; init; }
    public required string StoryTitle { get; init; }
    public required double TargetDuration { get; init; }
    public required IReadOnlyList<NarrativeBeat> Beats { get; init; }
    public required IReadOnlyDictionary<string, double> BeatTimingAllocation { get; init; }
    public IReadOnlyList<string> EmotionalCurve => Beats.Select(beat => beat.ViewerEmotion).ToArray();
    public IReadOnlyList<string> ViewerJourney => Beats.Select(beat => beat.ViewerQuestion).ToArray();
    public IReadOnlyList<string> EducationalProgression => Beats.Select(beat => beat.EducationalPriority).ToArray();
    public required double Confidence { get; init; }
}

public sealed record NarrativeTimelineReview
{
    public required IReadOnlyList<string> BeatOrder { get; init; }
    public required IReadOnlyDictionary<string, double> BeatDurations { get; init; }
    public required IReadOnlyList<string> ViewerJourney { get; init; }
    public required IReadOnlyList<string> EmotionalCurve { get; init; }
    public required IReadOnlyList<string> EducationalProgression { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> PlatformVariants { get; init; }
    public required IReadOnlyList<string> Recommendations { get; init; }
}

public interface INarrativeCompositionEngine
{
    NarrativeTimeline ComposeLong(VisualStory story, double targetDuration = NarrativeCompositionEngine.LongTargetDurationSeconds);
    NarrativeTimeline ComposeShort(VisualStory story, double targetDuration = NarrativeCompositionEngine.ShortTargetDurationSeconds);
    Task<NarrativeTimelineReview> WriteDiagnosticsAsync(NarrativeTimeline timeline, string outputFolder, CancellationToken cancellationToken = default);
}

public sealed class NarrativeCompositionEngine : INarrativeCompositionEngine
{
    public const string Version = "4.7A";
    public const double LongTargetDurationSeconds = 240;
    public const double ShortTargetDurationSeconds = 45;

    public NarrativeTimeline ComposeLong(VisualStory story, double targetDuration = LongTargetDurationSeconds)
    {
        var roles = new[] { NarrativeBeatRole.Hook, NarrativeBeatRole.Recognition, NarrativeBeatRole.ExplanationA, NarrativeBeatRole.ExplanationB, NarrativeBeatRole.ObservationA, NarrativeBeatRole.ObservationB, NarrativeBeatRole.InterestingFact, NarrativeBeatRole.Memory, NarrativeBeatRole.CallToAction };
        var weights = new[] { .08, .10, .16, .16, .13, .13, .09, .08, .07 };
        return Compose(story, NarrativeTimelineType.LongDocumentary, targetDuration, roles, weights);
    }

    public NarrativeTimeline ComposeShort(VisualStory story, double targetDuration = ShortTargetDurationSeconds)
    {
        var roles = new[] { NarrativeBeatRole.Hook, NarrativeBeatRole.Recognition, NarrativeBeatRole.Explanation, NarrativeBeatRole.Observation, NarrativeBeatRole.CallToAction };
        var weights = new[] { .20, .18, .26, .22, .14 };
        return Compose(story, NarrativeTimelineType.ShortDocumentary, targetDuration, roles, weights);
    }

    public async Task<NarrativeTimelineReview> WriteDiagnosticsAsync(NarrativeTimeline timeline, string outputFolder, CancellationToken cancellationToken = default)
    {
        var review = BuildReview(timeline);
        Directory.CreateDirectory(outputFolder);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "NarrativeTimelineReview.json"), JsonSerializer.Serialize(review, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true)), cancellationToken);
        return review;
    }

    private static NarrativeTimeline Compose(VisualStory story, NarrativeTimelineType type, double targetDuration, IReadOnlyList<NarrativeBeatRole> roles, IReadOnlyList<double> weights)
    {
        var durations = AllocateDurations(targetDuration, roles, weights);
        var beats = roles.Select(role => BuildBeat(story, type, role, durations[role.ToString()])).ToArray();
        return new NarrativeTimeline
        {
            TimelineId = $"narrative_{story.StoryId}_{type}".ToLowerInvariant(),
            TimelineType = type,
            StoryId = story.StoryId,
            StoryTitle = story.StoryTitle,
            TargetDuration = targetDuration,
            Beats = beats,
            BeatTimingAllocation = durations,
            Confidence = Math.Clamp(story.StoryConfidence, 0, 1)
        };
    }

    private static IReadOnlyDictionary<string, double> AllocateDurations(double targetDuration, IReadOnlyList<NarrativeBeatRole> roles, IReadOnlyList<double> weights)
    {
        var allocations = new Dictionary<string, double>();
        var running = 0d;
        for (var i = 0; i < roles.Count; i++)
        {
            var duration = i == roles.Count - 1 ? Math.Round(targetDuration - running, 2) : Math.Round(targetDuration * weights[i], 2);
            allocations[roles[i].ToString()] = duration;
            running += duration;
        }
        return allocations;
    }

    private static NarrativeBeat BuildBeat(VisualStory story, NarrativeTimelineType type, NarrativeBeatRole role, double duration)
    {
        var profile = BeatProfile(role, story);
        var variants = story.RecommendedPlatformVariations.Count == 0 ? ["landscape", "portrait", "square"] : story.RecommendedPlatformVariations.Keys.ToArray();
        return new NarrativeBeat
        {
            BeatId = $"{story.StoryId}_{type}_{role}".ToLowerInvariant(),
            BeatRole = role,
            BeatGoal = profile.Goal,
            ViewerQuestion = profile.Question,
            ViewerEmotion = profile.Emotion,
            TargetDuration = duration,
            VisualPriority = profile.Visual,
            NarrationPriority = profile.Narration,
            EducationalPriority = profile.Education,
            RecommendedComposition = story.RecommendedComposition,
            RecommendedPlatformVariants = variants,
            Confidence = Math.Clamp(story.StoryConfidence, 0, 1)
        };
    }

    private static (string Goal, string Question, string Emotion, string Visual, string Narration, string Education) BeatProfile(NarrativeBeatRole role, VisualStory story) => role switch
    {
        NarrativeBeatRole.Hook => ("Open the documentary question.", story.ViewerQuestion, story.EmotionalHook, story.PrimaryVisualSubject, "State the promise without resolving it.", "Introduce the observable mystery."),
        NarrativeBeatRole.Recognition => ("Help the viewer identify the story subject.", "What am I looking at?", "Recognition.", story.RecommendedViewerFocus, "Name the subject and viewing context.", "Establish the core astronomy event."),
        NarrativeBeatRole.ExplanationA => ("Explain the first causal idea.", "Why is this happening?", "Understanding.", story.VisualRelationship, "Explain the relationship in plain language.", story.PrimaryStory),
        NarrativeBeatRole.ExplanationB => ("Deepen the explanation.", "What does it mean scientifically?", "Clarity.", story.RecommendedNegativeSpace, "Clarify the science without extra prompt or image instructions.", story.ViewerTakeaway),
        NarrativeBeatRole.Explanation => ("Deliver the compact explanation.", "Why does it matter?", "Understanding.", story.VisualRelationship, "Condense the science into one fast documentary beat.", story.ViewerTakeaway),
        NarrativeBeatRole.ObservationA => ("Describe how to observe it.", "How can I see it?", "Anticipation.", story.EnvironmentRecommendation, "Give practical observation context.", "Translate the story into viewer action."),
        NarrativeBeatRole.ObservationB => ("Reinforce observation details.", "What should I notice?", "Focus.", story.LightingRecommendation, "Point attention to the most important observable detail.", "Connect observation back to the story relationship."),
        NarrativeBeatRole.Observation => ("Give the quick observing cue.", "What should I look for?", "Anticipation.", story.EnvironmentRecommendation, "Provide one concise observation instruction.", "Make the viewing takeaway usable."),
        NarrativeBeatRole.InterestingFact => ("Add a memorable factual turn.", "What is surprising here?", "Awe.", story.PrimaryVisualSubject, "Add one memorable fact anchored in the story.", "Increase retention with factual context."),
        NarrativeBeatRole.Memory => ("Make the story stick.", "What should I remember?", "Wonder.", story.RecommendedViewerFocus, "Restate the takeaway emotionally and accurately.", story.ViewerTakeaway),
        NarrativeBeatRole.CallToAction => ("Close with viewer action.", "What should I do next?", "Motivation.", string.Join(", ", story.RecommendedOverlayZones), "Invite the viewer to observe, save, or continue learning.", "End with a factual next step."),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static NarrativeTimelineReview BuildReview(NarrativeTimeline timeline) => new()
    {
        BeatOrder = timeline.Beats.Select(beat => beat.BeatRole.ToString()).ToArray(),
        BeatDurations = timeline.BeatTimingAllocation,
        ViewerJourney = timeline.ViewerJourney,
        EmotionalCurve = timeline.EmotionalCurve,
        EducationalProgression = timeline.EducationalProgression,
        PlatformVariants = timeline.Beats.ToDictionary(beat => beat.BeatRole.ToString(), beat => beat.RecommendedPlatformVariants),
        Recommendations = timeline.Beats.Select(beat => $"{beat.BeatRole}: {beat.BeatGoal}; composition: {beat.RecommendedComposition}").ToArray()
    };
}
