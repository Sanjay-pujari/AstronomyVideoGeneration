using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static class FinalNarrationDeduplicationPass
{
    public static QuestionDrivenNarrationDto Apply(QuestionDrivenNarrationDto narration)
    {
        ArgumentNullException.ThrowIfNull(narration);
        var repaired = SceneNarrationDuplicateValidator.ValidateAndRepair(narration);
        foreach (var scene in repaired.Scenes)
            ValidateUniqueSentenceCount($"{scene.SceneNumber}:{scene.QuestionType}", scene.NarrationText);
        return repaired;
    }

    public static VideoNarrationScriptDto Apply(VideoNarrationScriptDto script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var sceneScripts = script.SceneScripts.Select(scene =>
        {
            var deduplicated = SceneNarrationDuplicateValidator.RemoveDuplicates(scene.Narration);
            ValidateUniqueSentenceCount(scene.SceneKey, deduplicated);
            return string.Equals(deduplicated, scene.Narration, StringComparison.Ordinal)
                ? scene
                : scene with { Narration = deduplicated };
        }).ToArray();

        var fullNarrationText = string.Join(" ", sceneScripts.Select(scene => scene.Narration).Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
        return script with { SceneScripts = sceneScripts, FullNarrationText = fullNarrationText };
    }

    public static VideoTtsTimingsDto Apply(VideoTtsTimingsDto timings)
    {
        ArgumentNullException.ThrowIfNull(timings);

        var sceneTimings = timings.SceneTimings.Select(scene =>
        {
            var deduplicated = SceneNarrationDuplicateValidator.RemoveDuplicates(scene.Narration);
            ValidateUniqueSentenceCount(scene.SceneKey, deduplicated);
            return string.Equals(deduplicated, scene.Narration, StringComparison.Ordinal)
                ? scene
                : scene with { Narration = deduplicated };
        }).ToArray();

        return timings with { SceneTimings = sceneTimings };
    }

    public static LongFormVideoTtsTimingsDto Apply(LongFormVideoTtsTimingsDto timings)
    {
        ArgumentNullException.ThrowIfNull(timings);

        var sectionTimings = timings.SectionTimings.Select(section =>
        {
            var deduplicated = SceneNarrationDuplicateValidator.RemoveDuplicates(section.Narration);
            ValidateUniqueSentenceCount(section.SectionKey, deduplicated);
            return string.Equals(deduplicated, section.Narration, StringComparison.Ordinal)
                ? section
                : section with { Narration = deduplicated };
        }).ToArray();

        return timings with { SectionTimings = sectionTimings };
    }

    private static void ValidateUniqueSentenceCount(string sceneKey, string narration)
    {
        var sentences = SceneNarrationDuplicateValidator.SplitSentences(narration).ToArray();
        var uniqueSentenceCount = sentences.Select(sentence => sentence.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (uniqueSentenceCount != sentences.Length || SceneNarrationDuplicateValidator.HasDuplicateNarration(narration))
            throw new InvalidOperationException($"Final narration deduplication failed for scene '{sceneKey}': unique sentence count must equal total sentence count before subtitle generation.");
    }
}
