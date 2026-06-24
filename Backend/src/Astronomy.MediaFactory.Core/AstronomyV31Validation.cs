using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyV31Scene(string SceneId, string ScenePurpose, string NarrationText, string AudioPath, IReadOnlyList<string> SubtitleCues);
public sealed record AstronomyV31ValidationResult(bool Passed, IReadOnlyList<string> Errors);

public static class AstronomyV31Validation
{
    public static AstronomyV31ValidationResult ValidatePhase18V31(
        IReadOnlyList<AstronomyV31Scene> englishScenes,
        IReadOnlyList<AstronomyV31Scene> hindiScenes,
        IReadOnlyList<string> generatedMp3Paths,
        SubtitleTtsOptions subtitleOptions,
        FactExpansionResult? factExpansion,
        EventFamily family,
        int expectedSceneCount)
    {
        var errors = new List<string>();
        if (!string.Equals(subtitleOptions.TtsMode, "SceneLevel", StringComparison.OrdinalIgnoreCase)) errors.Add("SubtitleTtsOptions.TtsMode must remain SceneLevel.");
        if (englishScenes.Count != expectedSceneCount) errors.Add("English scene count does not match expected scene plan.");
        if (hindiScenes.Count != expectedSceneCount) errors.Add("Hindi scene count does not match expected scene plan.");
        var sceneAudio = englishScenes.Concat(hindiScenes).Select(s => Normalize(s.AudioPath)).Where(p => p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mp3 in generatedMp3Paths.Select(Normalize))
        {
            if (!sceneAudio.Contains(mp3)) errors.Add($"Cue-level or unknown MP3 generated: {mp3}");
            if (mp3.Contains("subtitle", StringComparison.OrdinalIgnoreCase) || mp3.Contains("cue-", StringComparison.OrdinalIgnoreCase)) errors.Add($"Cue-level MP3 naming is not allowed: {mp3}");
        }
        if (generatedMp3Paths.Count != sceneAudio.Count) errors.Add("TTS files must be scene-level only: one MP3 per scene.");
        foreach (var pair in englishScenes.Zip(hindiScenes))
            if (!string.Equals(pair.First.ScenePurpose, pair.Second.ScenePurpose, StringComparison.OrdinalIgnoreCase)) errors.Add($"ScenePurpose changed after Hindi generation for {pair.First.SceneId}.");
        var repeatedHindi = hindiScenes.GroupBy(s => NormalizeText(s.NarrationText)).Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Select(s => s.ScenePurpose).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1).ToArray();
        if (repeatedHindi.Length > 0) errors.Add("Hindi scenes are identical/repeated across different scene purposes.");
        if (IsV31Family(family) && factExpansion is null) errors.Add("FactExpansionResult is required for V3-enabled families.");
        if (subtitleOptions.SubtitleMaxCharsPerLine != 42 && subtitleOptions.SubtitleMaxCharsPerLine <= 0) errors.Add("Subtitle limits must come from valid SubtitleTtsOptions values.");
        return new AstronomyV31ValidationResult(errors.Count == 0, errors);
    }

    public static bool IsV31Family(EventFamily family) => family is EventFamily.PlanetGrouping or EventFamily.Meteor or EventFamily.Moon or EventFamily.Eclipse;
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static string NormalizeText(string text) => string.Join(' ', text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
