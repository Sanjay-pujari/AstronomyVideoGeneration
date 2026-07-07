using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record StoryFrameVisualReview
{
    public required string ReviewId { get; init; }
    public required string PlanId { get; init; }
    public required string FramePlanId { get; init; }
    public required string StoryId { get; init; }
    public required string AspectRatio { get; init; }
    public required string Platform { get; init; }
    public required int FrameCount { get; init; }
    public required IReadOnlyList<StoryFrameVisualReviewFrame> ReviewedFrames { get; init; }
    public required double StoryContinuityScore { get; init; }
    public required double PlatformNativeScore { get; init; }
    public required double DocumentaryScore { get; init; }
    public required double EducationalProgressionScore { get; init; }
    public required double VisualConsistencyScore { get; init; }
    public required double TypographySafetyScore { get; init; }
    public required double AstronomyAccuracyScore { get; init; }
    public required double OverallScore { get; init; }
    public required string Recommendation { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> CriticalIssues { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public sealed record StoryFrameVisualReviewFrame
{
    public required int FrameNumber { get; init; }
    public required NarrativeBeatRole BeatRole { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string ViewerEmotion { get; init; }
    public required string ExpectedVisualIntent { get; init; }
    public required string GeneratedFramePath { get; init; }
    public required string VisualContinuityNotes { get; init; }
    public required string PlatformCompositionNotes { get; init; }
    public required IReadOnlyList<string> Risks { get; init; }
    public required string Recommendation { get; init; }
}
