using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed class HeroAssetStoryGenerationRequest
{
    public HeroAssetStoryGenerationRequest()
    {
    }

    public HeroAssetStoryGenerationRequest(
        string EventId,
        string RegionId,
        string Language = "en",
        bool DryRun = true,
        bool OverwriteExisting = false,
        HeroAssetGenerationPhase Phase = HeroAssetGenerationPhase.HookSelection)
    {
        this.EventId = EventId;
        this.RegionId = RegionId;
        this.Language = Language;
        this.DryRun = DryRun;
        this.OverwriteExisting = OverwriteExisting;
        this.Phase = Phase.ToString();
    }

    public string EventId { get; set; } = string.Empty;

    public string RegionId { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public bool DryRun { get; set; } = true;

    public bool OverwriteExisting { get; set; }

    public string Phase { get; set; } = HeroAssetGenerationPhase.HookSelection.ToString();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HeroAssetGenerationPhase
{
    Story,
    HookSelection,
    Blueprint,
    SceneSelection,
    Images,
    Full
}

public sealed record HeroAssetStoryGenerationResponse(
    string EventId,
    bool IsValid,
    HeroAssetStoryDto HeroStory,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> GeneratedFiles);

public sealed record HeroAssetGenerationResponse(
    string EventId,
    bool IsValid,
    HeroAssetStoryDto HeroStory,
    string SelectedHook,
    IReadOnlyList<string> AlternativeHooks,
    IReadOnlyList<HeroHookScoreDto> HookScores,
    HeroAssetBlueprintDto HeroBlueprint,
    IReadOnlyList<HeroPlatformVariantDto> PlatformVariants,
    HeroAssetReviewScoresDto ReviewScores,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> GeneratedFiles,
    string PhaseRequested,
    string PhaseExecuted,
    bool StoryExecuted,
    bool BlueprintExecuted,
    bool ImageGenerationExecuted)
{
    public HeroSceneManifestDto? HeroSceneManifest { get; init; }

    public bool HeroSceneSelectorExecuted { get; init; }

    public bool HeroSceneManifestGenerated { get; init; }

    public string? HeroSceneManifestPath { get; init; }

    public string? PrimaryScene { get; init; }

    public string? SecondaryScene { get; init; }

    public string? SupportScene { get; init; }
}

public sealed record HeroAssetStoryDto(
    string EventId,
    string RegionId,
    string Language,
    string HeroHook,
    string HeroMessage,
    string HeroAction,
    string HeroVisualFocus,
    string HeroEmotion,
    string HeroPlatformIntent,
    HeroStorySourceDto HeroStorySource,
    HeroAssetStoryScoresDto Scores,
    int StoryScore,
    DateTimeOffset GeneratedUtc);

public sealed record HeroStorySourceDto(
    string What,
    string Where,
    string When,
    string Why);

public sealed record HeroAssetStoryScoresDto(
    int ScrollStoppingScore,
    int ClickabilityScore,
    int ShareabilityScore,
    int UnderstandabilityScore);

public sealed record HeroHookScoreDto(
    string Hook,
    int ScrollStoppingScore,
    int ClickabilityScore,
    int ShareabilityScore,
    int UnderstandabilityScore,
    int TotalScore);

public sealed record HeroAssetBlueprintFileDto(
    string EventId,
    string SelectedHook,
    HeroAssetBlueprintDto HeroBlueprint);

public sealed record HeroAssetBlueprintDto(
    string HeroEmotion,
    string LayoutStyle,
    string VisualFocus,
    string VisualNarrative,
    IReadOnlyList<HeroPlatformVariantDto> PlatformVariants);

public sealed record HeroPlatformVariantDto(
    string Variant,
    string Size,
    string Purpose,
    HeroLayoutBlueprintDto LayoutBlueprint);

public sealed record HeroLayoutBlueprintDto(
    string PrimaryTextPlacement,
    string CenterVisual,
    string SupportingTextPlacement,
    string Atmosphere);

public sealed record HeroAssetReviewScoresDto(
    int ScrollStoppingScore,
    int ClickabilityScore,
    int ShareabilityScore,
    int UnderstandabilityScore,
    int HeroAssetReadinessScore);

public interface IHeroAssetStoryGenerator
{
    Task<HeroAssetStoryGenerationResponse> GenerateHeroAssetStoryAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken);

    Task<HeroAssetGenerationResponse> GenerateHeroAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken);
}

public interface IHeroAssetIntelligenceEngine
{
    Task<HeroAssetStoryGenerationResponse> GenerateHeroAssetStoryAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken);

    Task<HeroAssetGenerationResponse> GenerateHeroAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken);
}
