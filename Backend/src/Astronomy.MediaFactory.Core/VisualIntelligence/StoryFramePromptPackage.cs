namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum StoryFramePromptProvider { AzureOpenAIImage }

public sealed record StoryFramePromptPackage
{
    public required string PackageId { get; init; }
    public required string PlanId { get; init; }
    public required string FramePlanId { get; init; }
    public required int FrameNumber { get; init; }
    public required NarrativeBeatRole BeatRole { get; init; }
    public required string AspectRatio { get; init; }
    public required string Platform { get; init; }
    public required string PositivePrompt { get; init; }
    public required string NegativePrompt { get; init; }
    public required string Provider { get; init; }
    public required string VisualTreatment { get; init; }
    public required string SafeAreaInstructions { get; init; }
    public required string TypographyInstructions { get; init; }
    public required IReadOnlyDictionary<string, object> Diagnostics { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public sealed record StoryFramePromptReview
{
    public required string PlanId { get; init; }
    public required int PromptCount { get; init; }
    public required string AspectRatio { get; init; }
    public required string Provider { get; init; }
    public required bool NoCroppingConfirmed { get; init; }
    public required bool EmbeddedTextProhibited { get; init; }
    public required IReadOnlyList<string> BeatCoverage { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
