namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailAssetGenerationRequest
{
    public string EventId { get; set; } = string.Empty;

    public string RegionId { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public string Phase { get; set; } = "Intelligence";

    public bool DryRun { get; set; } = true;

    public bool OverwriteExisting { get; set; }
}

public sealed record ThumbnailAssetGenerationResponse(
    string PhaseRequested,
    string PhaseExecuted,
    bool ThumbnailIntelligenceGenerated,
    string ThumbnailIntelligencePath,
    string SelectedThumbnailHook,
    int ThumbnailReadinessScore,
    IReadOnlyList<string> GeneratedFiles);

public sealed record ThumbnailIntelligenceDto(
    string EventId,
    string RegionId,
    string Language,
    string SelectedThumbnailHook,
    IReadOnlyList<string> AlternativeThumbnailHooks,
    IReadOnlyList<ThumbnailHookScoreDto> ThumbnailHookScores,
    string Emotion,
    string ClickIntent,
    string CuriosityAngle,
    string VisualFocus,
    string ThumbnailStyle,
    string RecommendedVisualSource,
    string RecommendedSourceScene,
    IReadOnlyList<string> AvoidText,
    ThumbnailCopyDto ThumbnailCopy,
    IReadOnlyList<ThumbnailPlatformTargetDto> PlatformTargets,
    ThumbnailReadinessScoresDto Scores,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedUtc);

public sealed record ThumbnailHookScoreDto(
    string Hook,
    int ClickabilityScore,
    int CuriosityScore,
    int EmotionalPullScore,
    int ClarityScore,
    int TotalScore);

public sealed record ThumbnailCopyDto(
    string PrimaryText,
    string SecondaryText,
    string MicroText);

public sealed record ThumbnailPlatformTargetDto(
    string Platform,
    string Size,
    string Intent);

public sealed record ThumbnailReadinessScoresDto(
    int ClickabilityScore,
    int CuriosityScore,
    int EmotionalPullScore,
    int ClarityScore,
    int ThumbnailReadinessScore);

public interface IThumbnailAssetIntelligenceService
{
    Task<ThumbnailAssetGenerationResponse> GenerateThumbnailAssetsAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken);
}
