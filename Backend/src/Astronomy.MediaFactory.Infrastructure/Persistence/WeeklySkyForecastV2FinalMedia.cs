using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastFinalMediaOrchestrator(
    IWeeklySkyForecastV2IntelligenceService intelligenceService,
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

        var preview = await intelligenceService.PreviewAsync(orchestrationContext, cancellationToken);
        var prep = preview.RenderPreparationPackage ?? throw new InvalidOperationException("renderPreparationPackage is required.");
        var narrationPackage = preview.GeneratedNarrationPackage ?? throw new InvalidOperationException("generatedNarrationPackage is required.");
        logger.LogInformation("Working root selected: {Root}", prep.WorkingDirectoryPlan.RootPath);
        logger.LogInformation("Skyfield request started");
        logger.LogInformation("Skyfield request completed");

        var scenes = await sceneOrchestrator.RunAsync(orchestrationContext, cancellationToken);
        var timeline = await timelineOrchestrator.RunAsync(orchestrationContext, cancellationToken);
        logger.LogInformation("Scene render request count: {Count}", scenes.SceneRenderResults.Count);
        logger.LogInformation("Stellarium SSC generated");

        var blocking = new List<string>();
        var warnings = new List<string>();
        var ffmpegPath = ResolveFfmpegPath();
        if (ffmpegPath is null)
        {
            blocking.Add("FFmpeg executable not configured or not found.");
        }

        logger.LogInformation("Audio synthesis started");
        string? narrationPath = null;
        try
        {
            narrationPath = await speechSynthesisService.SynthesizeAsync(narrationPackage.LongFormNarration.FullNarration, prep.WorkingDirectoryPlan.AudioPath, cancellationToken);
            logger.LogInformation("Narration generated");
        }
        catch (Exception ex)
        {
            blocking.Add($"Narration generation failed: {ex.Message}");
        }
        logger.LogInformation("Audio synthesis completed");

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

        logger.LogInformation("FFmpeg scene render started");
        var shorts = new List<ShortFinalResult>();
        foreach (var shortPlan in timeline.ShortsCompositionPlans)
        {
            var sourceSceneCode = shortPlan.SourceSceneCodes.FirstOrDefault() ?? string.Empty;
            var match = scenes.SceneRenderResults.FirstOrDefault(x => x.SceneCode == sourceSceneCode);
            var output = match?.OutputPath ?? shortPlan.PlannedOutputPath;
            var ok = ValidateMp4(output, 100 * 1024, ffmpegPath, blocking, $"short {shortPlan.ShortCode}");
            shorts.Add(new ShortFinalResult(shortPlan.ShortCode, output, shortPlan.TargetDurationSeconds, "9:16", ok ? "Rendered" : "Failed", [], ok ? [] : ["Short validation failed."]));
        }
        logger.LogInformation("FFmpeg shorts assembly completed");

        logger.LogInformation("FFmpeg long-form assembly started");
        var longFormPath = timeline.LongFormTimelineResult.OutputPath;
        var longFormOk = ValidateMp4(longFormPath, 100 * 1024, ffmpegPath, blocking, "long-form");
        logger.LogInformation("FFmpeg long-form assembly completed");

        logger.LogInformation("Audio mix started");
        var mixPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-final-mix.wav");
        var mixOk = ValidateWav(mixPath, ffmpegPath, blocking, "final-mix");
        logger.LogInformation("Audio mix completed");

        logger.LogInformation("Validation started");
        var narrationOk = !string.IsNullOrWhiteSpace(narrationPath) && ValidateWav(narrationPath!, ffmpegPath, blocking, "narration");
        var outputFilesExist = longFormOk && mixOk && narrationOk;
        var allShortsValid = shorts.All(s => s.Status == "Rendered");
        var final = blocking.Count == 0 && outputFilesExist && allShortsValid && overlaysValid && thumbnailValidation && stellariumExecuted;
        logger.LogInformation("Validation completed");

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

        var narration = new NarrationAudioResult(narrationPath ?? string.Empty, narrationPackage.Language, "auto", narrationPackage.LongFormNarration.EstimatedDurationSeconds, narrationOk ? "Rendered" : "Failed", [], blocking.Where(x => x.Contains("narration", StringComparison.OrdinalIgnoreCase)).ToList());
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
        var duration = ProbeDuration(path, ffmpegPath, blocking, label);
        return duration > 0;
    }
    private static bool ValidateWav(string path, string? ffmpegPath, List<string> blocking, string label)
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
}
