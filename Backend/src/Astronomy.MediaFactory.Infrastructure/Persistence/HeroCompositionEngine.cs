using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class HeroCompositionEngine : IHeroCompositionEngine
{
    public HeroCompositionModelDto ComposeHeroComposition(
        HeroAssetStoryDto heroStory,
        string selectedHook,
        HeroAssetBlueprintDto heroBlueprint,
        HeroSceneManifestDto sceneManifest,
        IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes)
    {
        ArgumentNullException.ThrowIfNull(heroStory);
        ArgumentNullException.ThrowIfNull(heroBlueprint);
        ArgumentNullException.ThrowIfNull(sceneManifest);
        ArgumentNullException.ThrowIfNull(approvedScenes);

        var hookText = Clean(string.IsNullOrWhiteSpace(selectedHook) ? heroStory.HeroHook : selectedHook);
        var visualScene = Clean(sceneManifest.PrimaryScene.SceneId);
        var directionScene = Clean(sceneManifest.SupportScene.SceneId);
        var timingScene = ResolveSceneId(approvedScenes, sceneNumber: 3, AstronomyQuestionTypes.When) ?? "scene-003";
        var ctaScene = ResolveSceneId(approvedScenes, sceneNumber: 6, AstronomyQuestionTypes.Action) ?? sceneManifest.SecondaryScene.SceneId;

        var directionText = ResolveDirectionText(heroStory, approvedScenes, sceneManifest.SupportScene.SceneId);
        var timingText = ResolveTimingText(heroStory, approvedScenes, timingScene);
        var ctaText = ResolveCtaText(heroStory, approvedScenes, ctaScene);

        var validation = BuildValidation(hookText, visualScene, directionText, timingText, ctaText);
        if (!validation.HookPresent || !validation.VisualPresent || !validation.DirectionPresent || !validation.TimingPresent || !validation.CtaPresent)
            throw new InvalidOperationException("Hero composition model is incomplete; hook, visual, timing, direction, and CTA blocks are required.");

        return new HeroCompositionModelDto(
            HookBlock: new HeroCompositionHookBlockDto(hookText),
            VisualBlock: new HeroCompositionSceneBlockDto(visualScene),
            DirectionBlock: new HeroCompositionTextBlockDto(directionScene, directionText),
            TimingBlock: new HeroCompositionTextBlockDto(timingScene, timingText),
            CtaBlock: new HeroCompositionTextBlockDto(ctaScene, ctaText),
            Validation: validation);
    }

    private static HeroCompositionValidationDto BuildValidation(string hookText, string visualScene, string directionText, string timingText, string ctaText)
    {
        var hookPresent = !string.IsNullOrWhiteSpace(hookText);
        var visualPresent = !string.IsNullOrWhiteSpace(visualScene);
        var directionPresent = !string.IsNullOrWhiteSpace(directionText);
        var timingPresent = !string.IsNullOrWhiteSpace(timingText);
        var ctaPresent = !string.IsNullOrWhiteSpace(ctaText);
        var presentCount = new[] { hookPresent, visualPresent, directionPresent, timingPresent, ctaPresent }.Count(present => present);
        return new HeroCompositionValidationDto(hookPresent, visualPresent, directionPresent, timingPresent, ctaPresent, presentCount == 5 ? 100 : presentCount * 20);
    }

    private static string ResolveDirectionText(HeroAssetStoryDto heroStory, IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes, string supportSceneId)
    {
        var candidate = FindScene(approvedScenes, supportSceneId) ?? FindSceneByQuestionType(approvedScenes, AstronomyQuestionTypes.Where);
        var source = FirstNonEmpty(candidate?.SourceAnswer, heroStory.HeroStorySource.Where, heroStory.HeroAction);
        return ExtractDirection(source);
    }

    private static string ResolveTimingText(HeroAssetStoryDto heroStory, IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes, string timingSceneId)
    {
        var candidate = FindScene(approvedScenes, timingSceneId) ?? FindSceneByQuestionType(approvedScenes, AstronomyQuestionTypes.When);
        var source = FirstNonEmpty(candidate?.SourceAnswer, heroStory.HeroStorySource.When, heroStory.HeroAction);
        return ExtractTiming(source);
    }

    private static string ResolveCtaText(HeroAssetStoryDto heroStory, IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes, string ctaSceneId)
    {
        var candidate = FindScene(approvedScenes, ctaSceneId) ?? FindSceneByQuestionType(approvedScenes, AstronomyQuestionTypes.Action);
        var source = FirstNonEmpty(candidate?.SourceAnswer, heroStory.HeroAction, heroStory.HeroStorySource.Why);
        return ExtractCta(source);
    }

    private static ApprovedHeroSceneCandidate? FindScene(IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes, string sceneId)
        => approvedScenes.FirstOrDefault(scene => string.Equals(scene.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));

    private static ApprovedHeroSceneCandidate? FindSceneByQuestionType(IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes, string questionType)
        => approvedScenes.FirstOrDefault(scene => string.Equals(scene.QuestionType, questionType, StringComparison.OrdinalIgnoreCase));

    private static string? ResolveSceneId(IReadOnlyList<ApprovedHeroSceneCandidate> approvedScenes, int sceneNumber, string questionType)
        => approvedScenes.FirstOrDefault(scene => string.Equals(scene.SceneId, $"scene-{sceneNumber:000}", StringComparison.OrdinalIgnoreCase))?.SceneId
            ?? FindSceneByQuestionType(approvedScenes, questionType)?.SceneId;

    private static string FirstNonEmpty(params string?[] values)
        => Clean(values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty);

    private static string ExtractDirection(string value)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
        if (cleaned.Contains("solar", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("eye protection", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("certified", StringComparison.OrdinalIgnoreCase)) return "SAFE SOLAR VIEWING";
        if (cleaned.Contains("east", StringComparison.OrdinalIgnoreCase)) return "EASTERN SKY";
        if (cleaned.Contains("southeast", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("south east", StringComparison.OrdinalIgnoreCase)) return "SOUTHEAST";
        if (cleaned.Contains("southwest", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("south west", StringComparison.OrdinalIgnoreCase)) return "SOUTHWEST";
        if (cleaned.Contains("west", StringComparison.OrdinalIgnoreCase)) return "WEST";
        if (cleaned.Contains("overhead", StringComparison.OrdinalIgnoreCase)) return "OVERHEAD";
        return CompactWords(cleaned.ToUpperInvariant(), 5);
    }

    private static string ExtractTiming(string value)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
        if (cleaned.Contains("max eclipse", StringComparison.OrdinalIgnoreCase)) return "MAX ECLIPSE";
        var twelveHour = System.Text.RegularExpressions.Regex.Match(cleaned, @"\b\d{1,2}:\d{2}\s*(?:AM|PM)\s*[A-Z]{2,4}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (twelveHour.Success) return twelveHour.Value.ToUpperInvariant();
        var twentyFourHour = System.Text.RegularExpressions.Regex.Match(cleaned, @"\b(?<hour>[01]?\d|2[0-3]):(?<minute>[0-5]\d)\s*(?<zone>\+05:30|IST)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (twentyFourHour.Success)
        {
            var hour = int.Parse(twentyFourHour.Groups["hour"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var minute = twentyFourHour.Groups["minute"].Value;
            var suffix = hour >= 12 ? "PM" : "AM";
            var hour12 = hour % 12;
            if (hour12 == 0) hour12 = 12;
            var zone = twentyFourHour.Groups["zone"].Success ? " IST" : string.Empty;
            return $"{hour12}:{minute} {suffix}{zone}".Trim().ToUpperInvariant();
        }
        return CompactWords(cleaned.ToUpperInvariant(), 6);
    }

    private static string CompactWords(string value, int maxWords)
    {
        var words = Clean(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(maxWords)).Trim(' ', '.', ',');
    }

    private static string ExtractCta(string value)
    {
        var cleaned = Clean(value);
        if (cleaned.Contains("step outside tonight", StringComparison.OrdinalIgnoreCase)) return "STEP OUTSIDE TONIGHT";
        if (cleaned.Contains("look west", StringComparison.OrdinalIgnoreCase)) return "LOOK WEST AFTER SUNSET";
        return cleaned.Length > 28 ? "STEP OUTSIDE TONIGHT" : cleaned.ToUpperInvariant();
    }

    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
}
