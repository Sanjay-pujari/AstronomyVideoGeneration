namespace Astronomy.MediaFactory.Api;

public static class WeeklySscSceneFinalizer
{
    public sealed record FinalSscScene(string SceneCode, string ScriptPath, string ScreenshotPath, IReadOnlySet<string> SourceSceneCodes);

    public static IReadOnlyList<FinalSscScene> Build(
        string scriptsDirectory,
        string scenesDirectory,
        IEnumerable<(string SceneCode, IEnumerable<string> SourceSceneCodes)> scenes)
    {
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.SceneCode)) continue;
            if (!merged.TryGetValue(scene.SceneCode, out var sources))
            {
                sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                merged[scene.SceneCode] = sources;
            }

            foreach (var source in scene.SourceSceneCodes.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                sources.Add(source);
            }
        }

        return merged
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new FinalSscScene(
                x.Key,
                Path.Combine(scriptsDirectory, $"{x.Key}.ssc"),
                Path.Combine(scenesDirectory, $"{x.Key}.png"),
                x.Value))
            .ToList();
    }
}
