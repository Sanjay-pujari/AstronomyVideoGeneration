namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailAiOptimizationRequest
{
    public required ThumbnailGenerationRequest GenerationRequest { get; init; }
    public string? PrimaryObject { get; init; }
    public string? SpecialEvent { get; init; }
    public string? Region { get; init; }
    public string? Language { get; init; }
    public string? SeoTitle { get; init; }
    public IReadOnlyCollection<string> TopPerformingHooks { get; init; } = [];
}

public sealed class ThumbnailAiOptimizationResult
{
    public IReadOnlyCollection<string> CandidateHooks { get; init; } = [];
    public IReadOnlyCollection<ThumbnailHookScore> Scores { get; init; } = [];
    public string SelectedHook { get; init; } = "";
    public IReadOnlyCollection<string> RejectedHooks { get; init; } = [];
    public string EmotionType { get; init; } = "wonder";
    public string Language { get; init; } = "en";
    public double AnalyticsInfluence { get; init; }
    public bool HallucinationDetected { get; init; }
}

public sealed class ThumbnailHookScore
{
    public string Hook { get; init; } = "";
    public double Score { get; init; }
    public string EmotionType { get; init; } = "wonder";
    public double Readability { get; init; }
    public double AstronomyAccuracy { get; init; }
    public string? RejectionReason { get; init; }
    public bool IsRejected => !string.IsNullOrWhiteSpace(RejectionReason);
}
