using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class RenderManifestBuilder
{
    public RenderPlan Build(RenderManifest manifest)
    {
        var scenes = BuildScenes(manifest);

        var concatBuilder = new StringBuilder();
        foreach (var scene in scenes)
        {
            concatBuilder.AppendLine($"file '{EscapeConcatValue(scene.VisualPath)}'");
            concatBuilder.AppendLine($"duration {scene.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (scenes.Count > 0)
        {
            concatBuilder.AppendLine($"file '{EscapeConcatValue(scenes[^1].VisualPath)}'");
        }

        var plan = new RenderPlan
        {
            Title = manifest.Title,
            AudioPath = manifest.AudioPath,
            OutputPath = manifest.OutputPath,
            Scenes = scenes,
            ConcatInputContent = concatBuilder.ToString(),
            ManifestJson = JsonSerializer.Serialize(new
            {
                manifest.Title,
                manifest.AudioPath,
                manifest.OutputPath,
                scenes = scenes.Select(s => new { s.Order, s.Caption, s.VisualPath, s.DurationSeconds })
            }, new JsonSerializerOptions { WriteIndented = true }),
            CaptionMetadataJson = JsonSerializer.Serialize(new
            {
                captions = scenes.Select(s => new { s.Order, s.Caption, s.DurationSeconds })
            }, new JsonSerializerOptions { WriteIndented = true }),
            SubtitleScaffold = BuildSubtitleScaffold(scenes)
        };

        return plan;
    }

    private static List<RenderPlanScene> BuildScenes(RenderManifest manifest)
    {
        var candidates = new List<RenderSceneCandidate>();

        if (!string.IsNullOrWhiteSpace(manifest.IntroVisualPath))
        {
            candidates.Add(new RenderSceneCandidate("Intro", manifest.IntroVisualPath, 3, "intro"));
        }

        for (var i = 0; i < manifest.Scenes.Count; i++)
        {
            var scene = manifest.Scenes[i];
            candidates.Add(new RenderSceneCandidate(
                string.IsNullOrWhiteSpace(scene.Caption) ? $"Scene {i + 1}" : scene.Caption,
                scene.VisualPath,
                scene.DurationSeconds > 0 ? scene.DurationSeconds : 6,
                "scene"));
        }

        if (!string.IsNullOrWhiteSpace(manifest.OutroVisualPath))
        {
            candidates.Add(new RenderSceneCandidate("Outro", manifest.OutroVisualPath, 3, "outro"));
        }

        var scenes = new List<RenderPlanScene>();
        var order = 0;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.VisualPath))
            {
                continue;
            }

            scenes.Add(new RenderPlanScene
            {
                Order = order,
                Caption = candidate.Caption,
                VisualPath = candidate.VisualPath,
                DurationSeconds = candidate.DurationSeconds,
                Segment = candidate.Segment
            });

            order++;
        }

        return scenes;
    }

    private static string BuildSubtitleScaffold(IReadOnlyCollection<RenderPlanScene> scenes)
    {
        var sb = new StringBuilder();
        var cursor = TimeSpan.Zero;
        var index = 1;
        foreach (var scene in scenes)
        {
            sb.AppendLine(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var start = cursor;
            var end = cursor.Add(TimeSpan.FromSeconds(scene.DurationSeconds));
            sb.AppendLine($"{FormatSrtTime(start)} --> {FormatSrtTime(end)}");
            sb.AppendLine(scene.Caption);
            sb.AppendLine();

            cursor = end;
            index++;
        }

        return sb.ToString();
    }

    private static string FormatSrtTime(TimeSpan time)
        => $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";

    private static string EscapeConcatValue(string value)
        => value.Replace("'", "'\\''", StringComparison.Ordinal);
}

file sealed record RenderSceneCandidate(string Caption, string VisualPath, int DurationSeconds, string Segment);

public sealed class RenderPlan
{
    public string Title { get; init; } = string.Empty;
    public string AudioPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public List<RenderPlanScene> Scenes { get; init; } = [];
    public string ConcatInputContent { get; init; } = string.Empty;
    public string ManifestJson { get; init; } = string.Empty;
    public string CaptionMetadataJson { get; init; } = string.Empty;
    public string SubtitleScaffold { get; init; } = string.Empty;
}

public sealed class RenderPlanScene
{
    public int Order { get; init; }
    public string Caption { get; init; } = string.Empty;
    public string VisualPath { get; init; } = string.Empty;
    public int DurationSeconds { get; init; }
    public string Segment { get; init; } = string.Empty;
}
