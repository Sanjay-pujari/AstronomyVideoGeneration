using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed record HeroAssetStoryGenerationRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false,
    HeroAssetGenerationPhase Phase = HeroAssetGenerationPhase.Full);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HeroAssetGenerationPhase
{
    Story,
    Blueprint,
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
    IReadOnlyList<string> GeneratedFiles);

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
    int OverallScore);

public sealed record HeroAssetBlueprintDto(
    string LayoutStyle,
    string VisualFocus,
    string TitlePlacement,
    string SubtitlePlacement,
    string DirectionCue,
    string Emotion,
    IReadOnlyList<HeroPlatformVariantDto> PlatformVariants);

public sealed record HeroPlatformVariantDto(
    string Variant,
    string Size,
    string Purpose);

public sealed record HeroAssetReviewScoresDto(
    int ScrollStoppingScore,
    int ClickabilityScore,
    int ShareabilityScore,
    int UnderstandabilityScore,
    int EmotionStrengthScore,
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
