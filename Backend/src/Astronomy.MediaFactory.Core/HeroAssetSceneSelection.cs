namespace Astronomy.MediaFactory.Core;

public sealed record HeroSceneManifestDto(
    string PrimaryScene,
    string SecondaryScene,
    string SupportScene);

public sealed record ApprovedHeroSceneCandidate(
    string SceneId,
    string? QuestionType,
    string? NarrativeIntent,
    string? VisualIntent,
    string? SourceAnswer,
    string? AssetPath);

public interface IHeroAssetSceneSelector
{
    HeroSceneManifestDto SelectHeroScenes(
        HeroAssetStoryDto heroStory,
        HeroAssetBlueprintDto heroBlueprint,
        IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes);
}
