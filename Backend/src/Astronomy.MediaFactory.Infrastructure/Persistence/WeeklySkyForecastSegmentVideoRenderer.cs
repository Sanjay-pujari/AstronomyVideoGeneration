using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastSegmentVideoRenderer(
    IContentPlanningService planning,
    IWeeklySkyForecastContextBuilder contextBuilder,
    IWeeklySkyForecastSegmentPlanner segmentPlanner,
    IWeeklySkyForecastSscScenePlanner scenePlanner,
    ICategoryOutputPathResolver pathResolver,
    IProcessRunner processRunner) : IWeeklySkyForecastSegmentVideoRenderer
{
    public async Task<WeeklySkyForecastSegmentVideoRenderResponse> RenderAsync(Guid contentGenerationPlanId, WeeklySkyForecastSegmentVideoRenderRequest request, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var steps = new List<CategoryProductionStepResult>();
        var rendered = 0;
        var skipped = 0;
        var failed = 0;

        var plan = await planning.GetPlanByIdAsync(contentGenerationPlanId, cancellationToken)
            ?? throw new KeyNotFoundException($"Content generation plan '{contentGenerationPlanId}' was not found.");
        if (!string.Equals(plan.ContentCategoryCode, "WeeklySkyForecast", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This endpoint only supports WeeklySkyForecast plans.");

        var weeklyRequest = new WeeklySkyForecastProductionRequest(plan.ContentCategoryCode, plan.Language, plan.RegionId, plan.RegionId, plan.ScheduledUtc ?? DateTimeOffset.UtcNow, false, false, false, true, false, false);
        var context = await contextBuilder.BuildAsync(weeklyRequest, cancellationToken);
        var segmentPlan = await segmentPlanner.BuildAsync(context, cancellationToken);
        var scenePlan = await scenePlanner.BuildAsync(context, segmentPlan, cancellationToken);
        var outputPaths = pathResolver.Resolve("WeeklySkyForecast", context.WeekStartDate, context.RegionId, contentGenerationPlanId);

        Directory.CreateDirectory(outputPaths.RootDirectory);
        Directory.CreateDirectory(outputPaths.ManifestsDirectory);
        var segmentVideoDir = Path.Combine(outputPaths.RootDirectory, "segments");
        Directory.CreateDirectory(segmentVideoDir);

        var narrationManifestPath = Path.Combine(outputPaths.ManifestsDirectory, "NarrationManifest.json");
        if (!File.Exists(narrationManifestPath)) throw new InvalidOperationException("NarrationManifest.json is required before segment rendering.");
        var narrationManifest = JsonSerializer.Deserialize<WeeklyNarrationManifest>(await File.ReadAllTextAsync(narrationManifestPath, cancellationToken))
            ?? throw new InvalidOperationException("Narration manifest not readable.");

        var audioBySegment = narrationManifest.Segments.ToDictionary(x => x.SegmentCode, StringComparer.OrdinalIgnoreCase);
        var visualAssetManifestPath = Path.Combine(outputPaths.ManifestsDirectory, "weekly-visual-assets-manifest.json");
        if (!File.Exists(visualAssetManifestPath)) throw new InvalidOperationException("weekly-visual-assets-manifest.json is required before segment rendering.");
        using var visualManifestDoc = JsonDocument.Parse(await File.ReadAllTextAsync(visualAssetManifestPath, cancellationToken));
        var visualAssetManifest = JsonSerializer.Deserialize<List<WeeklySkyForecastVisualAssetManifestItem>>(visualManifestDoc.RootElement.GetProperty("visualAssetManifest").GetRawText()) ?? [];
        var sceneAssetBySegment = visualAssetManifest
            .Where(x => !string.IsNullOrWhiteSpace(x.SegmentCode))
            .GroupBy(x => x.SegmentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var allSegments = segmentPlan.LongSegments.Concat(segmentPlan.ShortSegments).OrderBy(x => x.SegmentType).ThenBy(x => x.SortOrder).ToList();
        var segmentResults = new List<WeeklySkyForecastSegmentVideoRenderItem>(allSegments.Count);

        foreach (var segment in allSegments)
        {
            if (!audioBySegment.TryGetValue(segment.SegmentCode, out var audioMeta))
            {
                errors.Add($"Missing narration segment for {segment.SegmentCode}.");
                failed++;
                continue;
            }

            var audioPath = Path.Combine(outputPaths.NarrationDirectory, audioMeta.OutputFileName);
            if (!File.Exists(audioPath))
            {
                errors.Add($"Missing narration audio for {segment.SegmentCode}: {audioPath}");
                failed++;
                continue;
            }

            if (!sceneAssetBySegment.TryGetValue(segment.SegmentCode, out var sceneAsset))
            {
                errors.Add($"Missing linked scene for {segment.SegmentCode}.");
                failed++;
                continue;
            }

            var scenePath = sceneAsset.CapturedImagePath;
            if (!File.Exists(scenePath))
            {
                errors.Add($"Missing captured scene for {segment.SegmentCode}: {scenePath}");
                failed++;
                continue;
            }

            var outputVideoPath = Path.Combine(segmentVideoDir, $"{segment.SortOrder:00}-{segment.SegmentCode}.mp4");
            var subtitlePath = Path.Combine(segmentVideoDir, $"{segment.SortOrder:00}-{segment.SegmentCode}.srt");
            if (!request.OverwriteExisting && File.Exists(outputVideoPath) && new FileInfo(outputVideoPath).Length > 0)
            {
                skipped++;
                segmentResults.Add(new(segment.SegmentCode, outputVideoPath, audioPath, scenePath, subtitlePath, 0, "Skipped", null, 0, 0));
                continue;
            }

            var durationSeconds = await ProbeDurationAsync(audioPath, cancellationToken);
            await File.WriteAllTextAsync(subtitlePath, BuildSingleCueSrt(audioMeta.NarrationText, durationSeconds), cancellationToken);

            var ffmpegSw = Stopwatch.StartNew();
            var renderSw = Stopwatch.StartNew();
            var args = BuildFfmpegArgs(scenePath, audioPath, outputVideoPath, durationSeconds, request);
            var result = await processRunner.ExecuteAsync("ffmpeg", args, cancellationToken, TimeSpan.FromSeconds(240));
            ffmpegSw.Stop();
            renderSw.Stop();

            if (result.ExitCode != 0)
            {
                failed++;
                var err = string.IsNullOrWhiteSpace(result.StandardError) ? "ffmpeg failed." : result.StandardError;
                errors.Add($"Render failed for {segment.SegmentCode}: {err}");
                segmentResults.Add(new(segment.SegmentCode, outputVideoPath, audioPath, scenePath, subtitlePath, durationSeconds, "Failed", err, renderSw.ElapsedMilliseconds, ffmpegSw.ElapsedMilliseconds));
                continue;
            }

            rendered++;
            segmentResults.Add(new(segment.SegmentCode, outputVideoPath, audioPath, scenePath, subtitlePath, durationSeconds, "Rendered", null, renderSw.ElapsedMilliseconds, ffmpegSw.ElapsedMilliseconds));
        }

        var manifestPath = Path.Combine(outputPaths.ManifestsDirectory, "SegmentVideoManifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(segmentResults, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        steps.Add(new CategoryProductionStepResult("RenderWeeklySegments", failed > 0 ? "CompletedWithErrors" : "Completed", DateTime.UtcNow, DateTime.UtcNow, 1, null, null, []));

        return new(contentGenerationPlanId, failed == 0, rendered, skipped, failed, manifestPath, segmentResults, warnings, errors, steps);
    }

    private async Task<double> ProbeDurationAsync(string audioPath, CancellationToken cancellationToken)
    {
        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"";
        var result = await processRunner.ExecuteAsync("ffprobe", args, cancellationToken, TimeSpan.FromSeconds(30));
        if (result.ExitCode != 0 || !double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || duration <= 0)
            throw new InvalidOperationException($"Unable to probe audio duration for {audioPath}");
        return duration;
    }

    private static string BuildSingleCueSrt(string text, double durationSeconds)
        => $"1\n00:00:00,000 --> {FormatSrt(durationSeconds)}\n{text.Trim()}\n";

    private static string FormatSrt(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
    }

    private static string BuildFfmpegArgs(string imagePath, string audioPath, string outputPath, double durationSeconds, WeeklySkyForecastSegmentVideoRenderRequest request)
    {
        var vf = request.EnableZoomPan
            ? "zoompan=z='min(zoom+0.0008,1.08)':d=1:s=1920x1080:fps=30"
            : "scale=1920:1080";
        if (request.EnableFadeInOut)
        {
            var fadeOutStart = Math.Max(0, durationSeconds - request.FadeDurationSeconds);
            vf += $",fade=t=in:st=0:d={request.FadeDurationSeconds.ToString(CultureInfo.InvariantCulture)},fade=t=out:st={fadeOutStart.ToString(CultureInfo.InvariantCulture)}:d={request.FadeDurationSeconds.ToString(CultureInfo.InvariantCulture)}";
        }

        return $"-y -loop 1 -i \"{imagePath}\" -i \"{audioPath}\" -t {durationSeconds.ToString(CultureInfo.InvariantCulture)} -vf \"{vf}\" -c:v libx264 -pix_fmt yuv420p -r 30 -c:a aac -shortest \"{outputPath}\"";
    }
}
