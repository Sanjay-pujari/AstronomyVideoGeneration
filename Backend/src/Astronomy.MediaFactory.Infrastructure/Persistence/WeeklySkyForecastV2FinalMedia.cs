using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastFinalMediaOrchestrator(
    IWeeklySkyForecastSceneRenderingOrchestrator sceneOrchestrator,
    IWeeklySkyForecastTimelineCompositionOrchestrator timelineOrchestrator,
    ISpeechSynthesisService speechSynthesisService,
    ILogger<WeeklySkyForecastFinalMediaOrchestrator> logger) : IWeeklySkyForecastFinalMediaOrchestrator
{
    public async Task<FinalMediaPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
        => await RunAsync(new WeeklySkyForecastV2OrchestrationContext(
            ContentGenerationPlanId: contentGenerationPlanId ?? request.ContentGenerationPlanId ?? request.PipelineRunId ?? Guid.NewGuid(),
            PipelineRunId: request.PipelineRunId ?? contentGenerationPlanId ?? request.ContentGenerationPlanId ?? Guid.NewGuid(),
            WorkingDirectoryRoot: null,
            Request: request,
            ResolvedRegion: null,
            WeeklyForecast: null,
            SkyfieldSummary: null,
            EventIntelligence: null,
            GeneratedAtUtc: DateTime.UtcNow), cancellationToken);

    public async Task<FinalMediaPackage> RunAsync(WeeklySkyForecastV2OrchestrationContext orchestrationContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("Content plan created");
        logger.LogInformation("Pipeline run created");

        var preview = orchestrationContext.IntelligencePreviewResult
            ?? throw new InvalidOperationException("intelligencePreviewResult is required on orchestration context.");
        var prep = orchestrationContext.RenderPreparationPackage
            ?? preview.RenderPreparationPackage
            ?? throw new InvalidOperationException("renderPreparationPackage is required on orchestration context.");
        var narrationPackage = preview.GeneratedNarrationPackage ?? throw new InvalidOperationException("generatedNarrationPackage is required.");
        logger.LogInformation("Working root selected: {Root}", prep.WorkingDirectoryPlan.RootPath);
        logger.LogInformation("Skyfield request started");
        logger.LogInformation("Skyfield request completed");

        var scenes = orchestrationContext.SceneRenderingPackage ?? await sceneOrchestrator.RunAsync(orchestrationContext, cancellationToken);
        var timeline = orchestrationContext.TimelineCompositionPackage ?? await timelineOrchestrator.RunAsync(orchestrationContext, cancellationToken);
        logger.LogInformation("Scene render request count: {Count}", scenes.SceneRenderResults.Count);
        logger.LogInformation("Stellarium SSC generated");

        var blocking = new List<string>();
        var warnings = new List<string>();
        var ffmpegPath = ResolveFfmpegPath();
        if (ffmpegPath is null)
        {
            blocking.Add("FFmpeg executable not configured or not found.");
        }

        logger.LogInformation("Starting narration synthesis");
        string? narrationMp3Path = null;
        string? narrationWavPath = null;
        try
        {
            narrationMp3Path = await speechSynthesisService.SynthesizeAsync(narrationPackage.LongFormNarration.FullNarration, prep.WorkingDirectoryPlan.AudioPath, cancellationToken);
            narrationWavPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "narration.wav");
            var canonicalMp3Path = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "narration.mp3");
            if (!string.Equals(narrationMp3Path, canonicalMp3Path, StringComparison.OrdinalIgnoreCase) && File.Exists(narrationMp3Path))
            {
                File.Copy(narrationMp3Path, canonicalMp3Path, true);
                narrationMp3Path = canonicalMp3Path;
            }

            if (ffmpegPath is not null && File.Exists(narrationMp3Path))
            {
                RunProcess(ffmpegPath, $"-y -i \"{narrationMp3Path}\" -ac 2 -ar 48000 \"{narrationWavPath}\"", blocking, "narration wav transcode");
            }
            logger.LogInformation("Narration synthesis completed");
        }
        catch (Exception ex)
        {
            blocking.Add($"Narration generation failed: {ex.Message}");
        }

        var thumbnailPath = scenes.ThumbnailRenderResult?.OutputPath ?? prep.ThumbnailRenderPlan.PlannedOutputPath;
        logger.LogInformation("Thumbnail render started");
        var thumbnailValidation = ValidateImage(thumbnailPath, 2048, blocking, "thumbnail");
        logger.LogInformation("Thumbnail render completed");

        logger.LogInformation("Overlay render started");
        var overlaysValid = true;
        foreach (var ov in scenes.OverlayRenderResults)
        {
            overlaysValid &= ValidateImage(ov.OutputPath, 2048, blocking, $"overlay {ov.SceneCode}");
        }
        logger.LogInformation("Overlay render completed");

        var stellariumExecuted = scenes.StellariumRenderResults.Any(x => string.Equals(x.Status, "Rendered", StringComparison.OrdinalIgnoreCase));
        if (!stellariumExecuted)
        {
            warnings.Add("Stellarium capture not executed; scenes should not be marked Rendered.");
            logger.LogInformation("Stellarium capture skipped");
        }
        else
        {
            logger.LogInformation("Stellarium capture completed");
        }

        logger.LogInformation("Starting shorts render");
        var shorts = new List<ShortFinalResult>();
        foreach (var shortPlan in timeline.ShortsCompositionPlans)
        {
            var sourceSceneCode = shortPlan.SourceSceneCodes.FirstOrDefault() ?? string.Empty;
            var match = scenes.SceneRenderResults.FirstOrDefault(x => x.SceneCode == sourceSceneCode);
            var source = match?.OutputPath ?? shortPlan.PlannedOutputPath;
            var output = Path.Combine(prep.WorkingDirectoryPlan.ShortsPath, $"short-{shortPlan.ShortCode}.mp4");
            var encoded = ffmpegPath is not null && RenderShort(output, source, narrationMp3Path, shortPlan.TargetDurationSeconds, ffmpegPath, blocking, shortPlan.ShortCode);
            var ok = encoded && ValidateMp4(output, 10 * 1024, ffmpegPath, blocking, $"short {shortPlan.ShortCode}");
            shorts.Add(new ShortFinalResult(shortPlan.ShortCode, output, shortPlan.TargetDurationSeconds, "9:16", ok ? "Rendered" : "Failed", [], ok ? [] : ["Short validation failed."]));
        }
        logger.LogInformation("Shorts render completed");

        logger.LogInformation("Starting audio mix");
        var mixPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-final-mix.wav");
        var mixOk = BuildFinalMix(narrationWavPath, narrationMp3Path, mixPath, ffmpegPath, blocking);
        logger.LogInformation("Audio mix completed");

        logger.LogInformation("Starting long-form assembly");
        var longFormPath = Path.Combine(prep.WorkingDirectoryPlan.LongFormPath, "weekly-skyforecast-longform-draft.mp4");
        var longFormRendered = ffmpegPath is not null && AssembleLongForm(scenes.SceneRenderResults.Select(x => x.OutputPath).ToList(), mixPath, longFormPath, ffmpegPath, blocking);
        var longFormOk = longFormRendered && ValidateMp4(longFormPath, 10 * 1024, ffmpegPath, blocking, "long-form");
        logger.LogInformation("Long-form assembly completed");

        logger.LogInformation("Validation started");
        var narrationWavOk = !string.IsNullOrWhiteSpace(narrationWavPath) && ValidateWav(narrationWavPath!, ffmpegPath, blocking, "narration wav");
        var narrationMp3Ok = !string.IsNullOrWhiteSpace(narrationMp3Path) && ValidateAudio(narrationMp3Path!, ffmpegPath, blocking, "narration mp3");
        var narrationOk = narrationWavOk && narrationMp3Ok;
        var outputFilesExist = longFormOk && mixOk && narrationOk;
        var allShortsValid = shorts.All(s => s.Status == "Rendered");
        var final = blocking.Count == 0 && outputFilesExist && allShortsValid && overlaysValid && thumbnailValidation && stellariumExecuted;
        logger.LogInformation("Final validation completed");

        var validation = new FinalMediaValidation(
            final,
            narrationOk,
            mixOk,
            longFormOk,
            allShortsValid,
            thumbnailValidation,
            false,
            longFormOk,
            outputFilesExist,
            final,
            false,
            true,
            true,
            blocking,
            warnings,
            outputFilesExist,
            ffmpegPath is not null,
            stellariumExecuted,
            overlaysValid,
            thumbnailValidation,
            allShortsValid,
            longFormOk);

        var narration = new NarrationAudioResult(narrationWavPath ?? string.Empty, narrationPackage.Language, "auto", narrationPackage.LongFormNarration.EstimatedDurationSeconds, narrationOk ? "Rendered" : "Failed", [], blocking.Where(x => x.Contains("narration", StringComparison.OrdinalIgnoreCase)).ToList());
        var mix = new FinalAudioMixResult(mixPath, narrationPackage.LongFormNarration.EstimatedDurationSeconds, mixOk ? "Rendered" : "Failed", [], mixOk ? [] : ["Audio mix validation failed."]);
        var longForm = new FinalLongFormVideoResult(longFormPath, timeline.LongFormTimelineResult.TotalDurationSeconds, "1920x1080", 30, longFormOk ? "Rendered" : "Failed", [], longFormOk ? [] : ["Long-form validation failed."]);
        var thumbnail = new ThumbnailFinalResult(thumbnailPath, thumbnailValidation ? "Rendered" : "Failed", true, [], thumbnailValidation ? [] : ["Thumbnail validation failed."]);
        return new FinalMediaPackage(longForm, narration, new BackgroundMusicResult(null, "NoMusic", 0, "Skipped", [], []), mix, shorts, thumbnail, new SubtitleResult("", "", "Skipped", false, [], []), validation, new FinalMediaFreezeStatus(true, final, [], blocking, warnings));
    }

    private static string? ResolveFfmpegPath() => File.Exists("/usr/bin/ffmpeg") ? "/usr/bin/ffmpeg" : (File.Exists("/bin/ffmpeg") ? "/bin/ffmpeg" : null);
    private static bool ValidateMp4(string path, long minBytes, string? ffmpegPath, List<string> blocking, string label)
    {
        if (!ValidateBasic(path, minBytes, blocking, label)) return false;
        if (ffmpegPath is null) return false;
        var media = ProbeMedia(path, ffmpegPath, blocking, label);
        return media is { Duration: > 0, Width: > 0, Height: > 0 } && !string.IsNullOrWhiteSpace(media.VideoCodec);
    }
    private static bool ValidateWav(string path, string? ffmpegPath, List<string> blocking, string label)
    {
        if (!ValidateBasic(path, 10 * 1024, blocking, label)) return false;
        if (ffmpegPath is null) return false;
        return ProbeDuration(path, ffmpegPath, blocking, label) > 0;
    }
    private static bool ValidateAudio(string path, string? ffmpegPath, List<string> blocking, string label)
    {
        if (!ValidateBasic(path, 10 * 1024, blocking, label)) return false;
        if (ffmpegPath is null) return false;
        return ProbeDuration(path, ffmpegPath, blocking, label) > 0;
    }
    private static bool ValidateImage(string path, long minBytes, List<string> blocking, string label)
    {
        if (!ValidateBasic(path, minBytes, blocking, label)) return false;
        try { var img = SixLabors.ImageSharp.Image.Identify(path); return img is not null && img.Width > 0 && img.Height > 0; }
        catch (Exception ex) { blocking.Add($"{label}: image decode failed for '{path}'. {ex.Message}"); return false; }
    }
    private static bool ValidateBasic(string path, long minBytes, List<string> blocking, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { blocking.Add($"{label}: missing file '{path}'."); return false; }
        var len = new FileInfo(path).Length;
        if (len <= minBytes) { blocking.Add($"{label}: file too small '{path}' ({len} bytes)."); return false; }
        return true;
    }
    private static double ProbeDuration(string path, string ffmpegPath, List<string> blocking, string label)
    {
        var ffprobe = ffmpegPath.Replace("ffmpeg", "ffprobe", StringComparison.OrdinalIgnoreCase);
        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"";
        var psi = new ProcessStartInfo(ffprobe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        var p = Process.Start(psi)!;
        p.WaitForExit();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (p.ExitCode != 0 || !double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        { blocking.Add($"{label}: ffprobe failed for '{path}'. exit={p.ExitCode} stderr={stderr}"); return 0; }
        return duration;
    }

    private static bool BuildFinalMix(string? narrationWavPath, string? narrationMp3Path, string mixPath, string? ffmpegPath, List<string> blocking)
    {
        var source = File.Exists(narrationWavPath) ? narrationWavPath : narrationMp3Path;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) { blocking.Add("final-mix: narration source missing."); return false; }
        if (ffmpegPath is null) { blocking.Add("final-mix: ffmpeg unavailable."); return false; }
        return RunProcess(ffmpegPath, $"-y -i \"{source}\" -af loudnorm=I=-16:LRA=11:TP=-1.5 \"{mixPath}\"", blocking, "final-mix")
            && ValidateWav(mixPath, ffmpegPath, blocking, "final-mix");
    }

    private static bool AssembleLongForm(IReadOnlyList<string> scenePaths, string mixPath, string outputPath, string ffmpegPath, List<string> blocking)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var listPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "longform-concat.txt");
        var sb = new StringBuilder();
        foreach (var s in scenePaths.Where(File.Exists)) sb.AppendLine($"file '{s.Replace("'", "'\\''")}'");
        File.WriteAllText(listPath, sb.ToString());
        return RunProcess(ffmpegPath, $"-y -f concat -safe 0 -i \"{listPath}\" -i \"{mixPath}\" -map 0:v:0 -map 1:a:0 -c:v libx264 -pix_fmt yuv420p -r 30 -s 1920x1080 -c:a aac -shortest \"{outputPath}\"", blocking, "long-form-assembly");
    }

    private static bool RenderShort(string output, string source, string? narrationMp3, double durationSeconds, string ffmpegPath, List<string> blocking, string label)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var trim = Math.Max(1, durationSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        var audioInput = File.Exists(narrationMp3) ? $"-i \"{narrationMp3}\"" : string.Empty;
        var audioMap = File.Exists(narrationMp3) ? "-map 1:a:0" : "-an";
        return RunProcess(ffmpegPath, $"-y -i \"{source}\" {audioInput} -t {trim} -vf \"scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2\" -map 0:v:0 {audioMap} -c:v libx264 -pix_fmt yuv420p -r 30 -c:a aac -shortest \"{output}\"", blocking, $"short-{label}");
    }

    private static bool RunProcess(string executable, string args, List<string> blocking, string label)
    {
        var psi = new ProcessStartInfo(executable, args) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode == 0) return true;
        blocking.Add($"{label}: ffmpeg failed exit={p.ExitCode} stderr={p.StandardError.ReadToEnd()}");
        return false;
    }

    private sealed record MediaProbe(double Duration, int Width, int Height, string? VideoCodec);
    private static MediaProbe? ProbeMedia(string path, string ffmpegPath, List<string> blocking, string label)
    {
        var ffprobe = ffmpegPath.Replace("ffmpeg", "ffprobe", StringComparison.OrdinalIgnoreCase);
        var args = $"-v error -select_streams v:0 -show_entries stream=codec_name,width,height:format=duration -of csv=p=0:s=, \"{path}\"";
        var psi = new ProcessStartInfo(ffprobe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        var output = p.StandardOutput.ReadToEnd().Trim();
        if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) { blocking.Add($"{label}: ffprobe media probe failed for '{path}'."); return null; }
        var parts = output.Split(',');
        if (parts.Length < 4) { blocking.Add($"{label}: ffprobe media probe format invalid for '{path}'."); return null; }
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)) return null;
        if (!int.TryParse(parts[2], out var w)) return null;
        if (!int.TryParse(parts[3], out var h)) return null;
        return new MediaProbe(dur, w, h, parts[1]);
    }
}
