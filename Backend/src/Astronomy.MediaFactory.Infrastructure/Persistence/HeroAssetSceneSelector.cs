using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class HeroAssetSceneSelector : IHeroAssetSceneSelector
{
    private static readonly Regex TokenRegex = new("[a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "after", "above", "be", "because", "for", "in", "is", "it", "of", "on", "or", "the", "this", "to", "will", "with"
    };

    private static readonly SceneRoleProfile PrimaryProfile = new(
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["What"] = 70,
            ["Why"] = 45,
            ["Action"] = 25,
            ["Where"] = 20,
            ["When"] = 15,
            ["How"] = 10
        },
        ["hero", "focus", "visual", "headline", "event", "happening", "together", "close", "striking", "bright"]);

    private static readonly SceneRoleProfile SecondaryProfile = new(
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action"] = 70,
            ["How"] = 50,
            ["When"] = 40,
            ["Why"] = 30,
            ["What"] = 20,
            ["Where"] = 10
        },
        ["action", "step", "outside", "look", "find", "tonight", "watch", "do", "clear", "closing"]);

    private static readonly SceneRoleProfile SupportProfile = new(
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Where"] = 70,
            ["When"] = 55,
            ["How"] = 45,
            ["Why"] = 25,
            ["Action"] = 20,
            ["What"] = 10
        },
        ["where", "when", "west", "horizon", "time", "context", "orientation", "location", "guide", "support"]);

    public HeroSceneManifestDto SelectHeroScenes(
        HeroAssetStoryDto heroStory,
        HeroAssetBlueprintDto heroBlueprint,
        IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes)
    {
        ArgumentNullException.ThrowIfNull(heroStory);
        ArgumentNullException.ThrowIfNull(heroBlueprint);
        ArgumentNullException.ThrowIfNull(approvedScenes);

        var candidates = approvedScenes
            .Where(scene => !string.IsNullOrWhiteSpace(scene.SceneId))
            .GroupBy(scene => scene.SceneId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(scene => ParseSceneNumber(scene.SceneId))
            .ThenBy(scene => scene.SceneId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length < 3)
            throw new ArgumentException("Hero scene selection requires at least three approved scene outputs.", nameof(approvedScenes));

        var storyTokens = ExtractTokens(string.Join(' ', heroStory.HeroHook, heroStory.HeroMessage, heroStory.HeroAction, heroStory.HeroVisualFocus, heroStory.HeroEmotion, heroBlueprint.VisualFocus, heroBlueprint.VisualNarrative));
        var primary = SelectBest(candidates, new HashSet<string>(StringComparer.OrdinalIgnoreCase), PrimaryProfile, storyTokens);
        var secondary = SelectBest(candidates, new HashSet<string>([primary.SceneId], StringComparer.OrdinalIgnoreCase), SecondaryProfile, storyTokens);
        var support = SelectBest(candidates, new HashSet<string>([primary.SceneId, secondary.SceneId], StringComparer.OrdinalIgnoreCase), SupportProfile, storyTokens);

        return new HeroSceneManifestDto(primary.SceneId, secondary.SceneId, support.SceneId);
    }

    private static ApprovedHeroSceneCandidate SelectBest(
        IReadOnlyList<ApprovedHeroSceneCandidate> candidates,
        IReadOnlySet<string> excludedSceneIds,
        SceneRoleProfile profile,
        IReadOnlySet<string> storyTokens)
        => candidates
            .Where(scene => !excludedSceneIds.Contains(scene.SceneId))
            .Select(scene => new ScoredScene(scene, ScoreScene(scene, profile, storyTokens)))
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => ParseSceneNumber(scored.Scene.SceneId))
            .ThenBy(scored => scored.Scene.SceneId, StringComparer.OrdinalIgnoreCase)
            .First()
            .Scene;

    private static int ScoreScene(ApprovedHeroSceneCandidate scene, SceneRoleProfile profile, IReadOnlySet<string> storyTokens)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(scene.QuestionType) && profile.QuestionTypeWeights.TryGetValue(scene.QuestionType, out var questionWeight))
            score += questionWeight;

        var sceneText = string.Join(' ', scene.QuestionType, scene.NarrativeIntent, scene.VisualIntent, scene.SourceAnswer);
        var sceneTokens = ExtractTokens(sceneText);
        score += sceneTokens.Intersect(storyTokens, StringComparer.OrdinalIgnoreCase).Count() * 3;
        score += profile.IntentKeywords.Count(keyword => sceneTokens.Contains(keyword)) * 5;
        return score;
    }

    private static HashSet<string> ExtractTokens(string? value)
        => TokenRegex.Matches(value ?? string.Empty)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int ParseSceneNumber(string sceneId)
    {
        var match = Regex.Match(sceneId, @"(\d+)$");
        return match.Success && int.TryParse(match.Value, out var number) ? number : int.MaxValue;
    }

    private sealed record SceneRoleProfile(
        IReadOnlyDictionary<string, int> QuestionTypeWeights,
        IReadOnlyList<string> IntentKeywords);

    private sealed record ScoredScene(ApprovedHeroSceneCandidate Scene, int Score);
}
