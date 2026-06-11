namespace Astronomy.MediaFactory.Core;

public sealed record HeroSceneManifestDto(
    string EventId,
    HeroSceneManifestEntryDto PrimaryScene,
    HeroSceneManifestEntryDto SecondaryScene,
    HeroSceneManifestEntryDto SupportScene,
    string SelectionReason)
{
    public string? PlanId { get; init; }

    public string? EventTitle { get; init; }

    public string? EventType { get; init; }

    public string? StrategyId { get; init; }

    public string? StrategyEventType { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string PrimarySceneId => PrimaryScene.SceneId;

    [System.Text.Json.Serialization.JsonIgnore]
    public string SecondarySceneId => SecondaryScene.SceneId;

    [System.Text.Json.Serialization.JsonIgnore]
    public string SupportSceneId => SupportScene.SceneId;
}

public sealed record HeroSceneManifestEntryDto(
    int SceneNumber,
    string SceneKey,
    string ImagePath,
    string Role)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string SceneId => $"scene-{SceneNumber:000}";
}

public sealed record ApprovedHeroSceneCandidate(
    string SceneId,
    string? QuestionType,
    string? NarrativeIntent,
    string? VisualIntent,
    string? SourceAnswer,
    string? AssetPath);

public interface IHeroAssetSceneSelector
{
    Task<HeroSceneManifestDto> SelectHeroScenesAsync(
        HeroAssetStoryGenerationRequest request,
        HeroAssetStoryDto heroStory,
        HeroAssetBlueprintDto heroBlueprint,
        IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes,
        CancellationToken cancellationToken = default);

    HeroSceneManifestDto SelectHeroScenes(
        HeroAssetStoryDto heroStory,
        HeroAssetBlueprintDto heroBlueprint,
        IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes);
}
