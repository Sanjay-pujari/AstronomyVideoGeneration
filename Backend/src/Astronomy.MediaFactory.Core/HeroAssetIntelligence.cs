namespace Astronomy.MediaFactory.Core;

public sealed record HeroAssetStoryGenerationRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record HeroAssetStoryGenerationResponse(
    string EventId,
    bool IsValid,
    HeroAssetStoryDto HeroStory,
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

public interface IHeroAssetStoryGenerator
{
    Task<HeroAssetStoryGenerationResponse> GenerateHeroAssetStoryAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken);
}

public interface IHeroAssetIntelligenceEngine
{
    Task<HeroAssetStoryGenerationResponse> GenerateHeroAssetStoryAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken);
}
