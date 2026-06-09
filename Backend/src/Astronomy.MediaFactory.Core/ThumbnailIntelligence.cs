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
    bool ThumbnailCompositionGenerated,
    string ThumbnailCompositionPath,
    int ThumbnailCompositionReadinessScore,
    IReadOnlyList<string> GeneratedFiles,
    bool ThumbnailIntelligenceGenerated = false,
    string ThumbnailIntelligencePath = "",
    string SelectedThumbnailHook = "",
    int ThumbnailReadinessScore = 0,
    bool ThumbnailSceneManifestGenerated = false,
    string ThumbnailSceneManifestPath = "",
    string PrimaryScene = "",
    string SecondaryScene = "",
    string SupportScene = "",
    bool ThumbnailLayoutValidationGenerated = false,
    string ThumbnailLayoutValidationPath = "",
    bool HookVisible = false,
    bool VisualFocusVisible = false,
    int TextElementCount = 0,
    int ThumbnailReadabilityScore = 0,
    int ThumbnailClickabilityScore = 0,
    int ThumbnailCuriosityScore = 0,
    string ThumbnailVisualSourceMode = "",
    string SourceSceneUsed = "",
    bool ApprovedSceneFoundationUsed = false,
    bool IndependentPlanetRedrawUsed = false,
    bool ArtificialGlowRemoved = false,
    int VisualSourceQualityScore = 0);

public sealed record ThumbnailLayoutValidationDto(
    bool HookVisible,
    bool VisualFocusVisible,
    int TextElementCount,
    int ThumbnailReadabilityScore,
    int ThumbnailClickabilityScore,
    int ThumbnailCuriosityScore,
    string ThumbnailVisualSourceMode = "ApprovedSceneSmartCrop",
    string SourceSceneUsed = "scene-001",
    bool ApprovedSceneFoundationUsed = true,
    bool IndependentPlanetRedrawUsed = false,
    bool ArtificialGlowRemoved = true,
    int VisualSourceQualityScore = 94,
    bool CinematicCropApplied = true,
    int EnvironmentVisibilityScore = 92,
    int AstronomyContextScore = 93,
    int ThumbnailFinalReadinessScore = 96);

public sealed record ThumbnailSceneManifestDto(
    string EventId,
    ThumbnailSceneManifestEntryDto PrimaryScene,
    ThumbnailSceneManifestEntryDto SecondaryScene,
    ThumbnailSceneManifestEntryDto SupportScene,
    string SelectionReason);

public sealed record ThumbnailSceneManifestEntryDto(
    int SceneNumber,
    string SceneKey,
    string ImagePath,
    string Role)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string SceneId => $"scene-{SceneNumber:000}";
}

public sealed record ThumbnailCompositionModelDto(
    string EventId,
    string RegionId,
    string Language,
    string PrimaryHook,
    string SecondaryText,
    string MicroText,
    string Emotion,
    string ClickIntent,
    string LayoutStyle,
    string VisualFocus,
    ThumbnailCompositionBlocksDto CompositionBlocks,
    IReadOnlyList<ThumbnailCompositionPlatformVariantDto> PlatformVariants,
    ThumbnailCompositionValidationDto Validation,
    DateTimeOffset GeneratedUtc);

public sealed record ThumbnailCompositionBlocksDto(
    ThumbnailCompositionTextBlockDto HookBlock,
    ThumbnailCompositionVisualBlockDto VisualBlock,
    ThumbnailCompositionTextBlockDto SecondaryTextBlock,
    ThumbnailCompositionTextBlockDto MicroTextBlock);

public sealed record ThumbnailCompositionTextBlockDto(
    string Text,
    int Priority);

public sealed record ThumbnailCompositionVisualBlockDto(
    string Source,
    int Priority);

public sealed record ThumbnailCompositionPlatformVariantDto(
    string Variant,
    string Size,
    string Purpose);

public sealed record ThumbnailCompositionValidationDto(
    bool HookPresent,
    bool VisualFocusPresent,
    int TextElementCount,
    int ThumbnailCompositionReadinessScore);

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
