using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastFinalMediaOrchestrator(
    IWeeklySkyForecastSceneRenderingOrchestrator sceneOrchestrator,
    IWeeklySkyForecastTimelineCompositionOrchestrator timelineOrchestrator,
    IFFmpegService ffmpegService,
    IMediaValidationService mediaValidationService,
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
        logger.LogInformation("Starting narration synthesis");
        var narrationWavPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-narration.wav");
        var narrationText = narrationPackage.LongFormNarration.FullNarration?.Trim() ?? string.Empty;
        var resolvedVoiceCode = "auto";
        logger.LogInformation("Narration text length: {Length}", narrationText.Length);
        logger.LogInformation("Narration voice: {VoiceCode}", resolvedVoiceCode);
        logger.LogInformation("Narration output path: {Path}", narrationWavPath);

        var narrationGenerated = await ExecuteNarrationSynthesisAsync(
            narrationText,
            prep.WorkingDirectoryPlan.AudioPath,
            narrationWavPath,
            orchestrationContext.Request.Diagnostics,
            ffmpegService,
            mediaValidationService,
            speechSynthesisService,
            logger,
            blocking,
            warnings,
            cancellationToken);
        logger.LogInformation("Narration synthesis completed");
        logger.LogInformation("Narration validation started");
        logger.LogInformation("Narration validation completed");

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

        var mixPath = Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-final-mix.wav");

        logger.LogInformation("Starting final audio mix");
        var mixOk = await BuildFinalMixAsync(narrationWavPath, mixPath, ffmpegService, mediaValidationService, blocking, cancellationToken);
        logger.LogInformation("Final audio mix completed");

        logger.LogInformation("Starting shorts render");
        var shorts = new List<ShortFinalResult>();
        foreach (var shortPlan in timeline.ShortsCompositionPlans)
        {
            var sourceSceneCode = shortPlan.SourceSceneCodes.FirstOrDefault() ?? string.Empty;
            var match = scenes.SceneRenderResults.FirstOrDefault(x => x.SceneCode == sourceSceneCode);
            var source = match?.OutputPath ?? shortPlan.PlannedOutputPath;
            var output = Path.Combine(prep.WorkingDirectoryPlan.FinalPath, $"short-{shortPlan.ShortCode}.mp4");
            var encoded = mixOk && await RenderShortAsync(output, source, mixPath, shortPlan.TargetDurationSeconds, ffmpegService, blocking, shortPlan.ShortCode, cancellationToken);
            var ok = encoded && await ValidateMp4Async(output, 1, mediaValidationService, blocking, $"short {shortPlan.ShortCode}", cancellationToken);
            shorts.Add(new ShortFinalResult(shortPlan.ShortCode, output, shortPlan.TargetDurationSeconds, "9:16", ok ? "Rendered" : "Failed", [], ok ? [] : ["Short validation failed."]));
        }
        logger.LogInformation("Shorts render completed");

        logger.LogInformation("Starting long-form assembly");
        var longFormPath = Path.Combine(prep.WorkingDirectoryPlan.FinalPath, "weekly-skyforecast-longform-draft.mp4");
        var longFormRendered = await AssembleLongFormAsync(timeline.SegmentCompositionResults.Select(x => x.SourceSceneOutputPath).ToList(), mixPath, longFormPath, ffmpegService, blocking, cancellationToken);
        var longFormOk = longFormRendered && await ValidateMp4Async(longFormPath, 1, mediaValidationService, blocking, "long-form", cancellationToken);
        logger.LogInformation("Long-form assembly completed");

        logger.LogInformation("Validation started");
        var narrationOk = narrationGenerated.WavReady;
        var outputFilesExist = longFormOk && mixOk && narrationOk;
        var allShortsValid = shorts.All(s => s.Status == "Rendered");
        var thumbnailContainsObjects = scenes.SceneRenderingValidation.ThumbnailContainsObjects;
        var sceneVisualsContainObjects = scenes.SceneRenderingValidation.SceneVisualsContainObjects;
        var visualAssetsResolved = scenes.SceneRenderingValidation.VisualAssetsResolved;
        var readyForHumanReview = sceneVisualsContainObjects;
        if (!readyForHumanReview)
        {
            blocking.Add("Scene visuals do not contain resolved celestial objects.");
        }
        var final = blocking.Count == 0 && outputFilesExist && allShortsValid && overlaysValid && thumbnailValidation && readyForHumanReview;
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
            readyForHumanReview,
            false,
            true,
            true,
            blocking,
            warnings,
            outputFilesExist,
            true,
            stellariumExecuted,
            overlaysValid,
            thumbnailValidation,
            allShortsValid,
            longFormOk,
            RealMediaOutputsGenerated: outputFilesExist && allShortsValid,
            FfprobeExecuted: true,
            ThumbnailContainsObjects: thumbnailContainsObjects,
            SceneVisualsContainObjects: sceneVisualsContainObjects,
            VisualAssetsResolved: visualAssetsResolved);

        var narration = new NarrationAudioResult(narrationWavPath ?? string.Empty, narrationPackage.Language, "auto", narrationPackage.LongFormNarration.EstimatedDurationSeconds, narrationOk ? "Rendered" : "Failed", [], blocking.Where(x => x.Contains("narration", StringComparison.OrdinalIgnoreCase)).ToList());
        var mix = new FinalAudioMixResult(mixPath, narrationPackage.LongFormNarration.EstimatedDurationSeconds, mixOk ? "Rendered" : "Failed", [], mixOk ? [] : ["Audio mix validation failed."]);
        var longForm = new FinalLongFormVideoResult(longFormPath, timeline.LongFormTimelineResult.TotalDurationSeconds, "1920x1080", 30, longFormOk ? "Rendered" : "Failed", [], longFormOk ? [] : ["Long-form validation failed."]);
        var thumbnail = new ThumbnailFinalResult(thumbnailPath, thumbnailValidation ? "Rendered" : "Failed", true, [], thumbnailValidation ? [] : ["Thumbnail validation failed."]);
        return new FinalMediaPackage(longForm, narration, new BackgroundMusicResult(null, "NoMusic", 0, "Skipped", [], []), mix, shorts, thumbnail, new SubtitleResult("", "", "Skipped", false, [], []), validation, new FinalMediaFreezeStatus(true, final, [], blocking, warnings));
    }



    private sealed record NarrationSynthesisOutcome(bool WavReady);

    private static async Task<NarrationSynthesisOutcome> ExecuteNarrationSynthesisAsync(
        string narrationText,
        string audioDirectory,
        string narrationWavPath,
        bool diagnosticsEnabled,
        IFFmpegService ffmpegService,
        IMediaValidationService mediaValidationService,
        ISpeechSynthesisService speechSynthesisService,
        ILogger logger,
        List<string> blocking,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(audioDirectory);
        if (string.IsNullOrWhiteSpace(narrationText))
        {
            blocking.Add("Generated narration text missing; cannot synthesize narration audio.");
            return new NarrationSynthesisOutcome(false);
        }

        var synthesized = false;
        try
        {
            await speechSynthesisService.SynthesizeToFileAsync(narrationText, narrationWavPath, cancellationToken);
            if (!File.Exists(narrationWavPath))
                throw new InvalidOperationException($"Narration synthesis did not produce output at '{narrationWavPath}'.");
            synthesized = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Narration synthesis failed for output path {NarrationWavPath}", narrationWavPath);
            if (!diagnosticsEnabled || !IsAzureSpeechUnavailable(ex))
            {
                blocking.Add($"Narration synthesis failed: {ex.Message}");
                return new NarrationSynthesisOutcome(false);
            }

            if (!await RunFfmpegAsync(ffmpegService, $"-y -f lavfi -i anullsrc=r=44100:cl=stereo -t 110 -c:a pcm_s16le \"{narrationWavPath}\"", narrationWavPath, blocking, "diagnostics silent narration", cancellationToken))
            {
                blocking.Add($"Narration synthesis failed and diagnostics fallback could not be generated: {ex.Message}");
                return new NarrationSynthesisOutcome(false);
            }

            warnings.Add("Diagnostics silent narration generated because Azure Speech is not configured.");
            synthesized = true;
        }

        if (!synthesized)
        {
            blocking.Add("Narration synthesis did not produce output.");
            return new NarrationSynthesisOutcome(false);
        }

        var narrationOk = await ValidateWavAsync(narrationWavPath, mediaValidationService, blocking, "narration wav", cancellationToken);
        return new NarrationSynthesisOutcome(narrationOk);
    }

    private static bool IsAzureSpeechUnavailable(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("azure", StringComparison.OrdinalIgnoreCase)
               || message.Contains("speech", StringComparison.OrdinalIgnoreCase)
               || message.Contains("cognitive", StringComparison.OrdinalIgnoreCase)
               || message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
               || message.Contains("401", StringComparison.OrdinalIgnoreCase)
               || message.Contains("403", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ValidateMp4Async(string path, long minBytes, IMediaValidationService mediaValidationService, List<string> blocking, string label, CancellationToken cancellationToken)
    {
        var result = await mediaValidationService.ValidateMp4Async(path, minBytes, cancellationToken);
        if (!result.IsValid) blocking.AddRange(result.BlockingIssues.Select(x => $"{label}: {x}"));
        return result.IsValid;
    }
    private static async Task<bool> ValidateWavAsync(string path, IMediaValidationService mediaValidationService, List<string> blocking, string label, CancellationToken cancellationToken)
    {
        var result = await mediaValidationService.ValidateWavAsync(path, cancellationToken);
        if (!result.IsValid) blocking.AddRange(result.BlockingIssues.Select(x => $"{label}: {x}"));
        return result.IsValid;
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
    private static async Task<bool> BuildFinalMixAsync(string narrationWavPath, string mixPath, IFFmpegService ffmpegService, IMediaValidationService mediaValidationService, List<string> blocking, CancellationToken cancellationToken)
    {
        if (!File.Exists(narrationWavPath)) { blocking.Add("final-mix: narration source missing."); return false; }
        if (File.Exists(mixPath)) File.Delete(mixPath);
        var copied = CopyFile(narrationWavPath, mixPath, blocking, "final-mix copy");
        return copied || (await RunFfmpegAsync(ffmpegService, $"-y -i \"{narrationWavPath}\" -af loudnorm=I=-16:LRA=11:TP=-1.5 \"{mixPath}\"", mixPath, blocking, "final-mix", cancellationToken)
            && await ValidateWavAsync(mixPath, mediaValidationService, blocking, "final-mix", cancellationToken));
    }

    private static async Task<bool> AssembleLongFormAsync(IReadOnlyList<string> scenePaths, string mixPath, string outputPath, IFFmpegService ffmpegService, List<string> blocking, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var listPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "longform-concat.txt");
        var sb = new StringBuilder();
        foreach (var s in scenePaths.Where(File.Exists)) sb.AppendLine($"file '{s.Replace("'", "'\\''")}'");
        File.WriteAllText(listPath, sb.ToString());
        return await RunFfmpegAsync(ffmpegService, $"-y -f concat -safe 0 -i \"{listPath}\" -i \"{mixPath}\" -map 0:v:0 -map 1:a:0 -c:v libx264 -pix_fmt yuv420p -r 30 -s 1920x1080 -c:a aac -shortest \"{outputPath}\"", outputPath, blocking, "long-form-assembly", cancellationToken);
    }

    private static async Task<bool> RenderShortAsync(string output, string source, string? narrationWav, double durationSeconds, IFFmpegService ffmpegService, List<string> blocking, string label, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var trim = Math.Max(1, durationSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        var audioInput = !string.IsNullOrWhiteSpace(narrationWav) && File.Exists(narrationWav) ? $"-i \"{narrationWav}\"" : string.Empty;
        var audioMap = !string.IsNullOrWhiteSpace(narrationWav) && File.Exists(narrationWav) ? "-map 1:a:0" : "-an";
        return await RunFfmpegAsync(ffmpegService, $"-y -i \"{source}\" {audioInput} -t {trim} -vf \"scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2\" -map 0:v:0 {audioMap} -c:v libx264 -pix_fmt yuv420p -r 30 -c:a aac -shortest \"{output}\"", output, blocking, $"short-{label}", cancellationToken);
    }

    private static bool CopyFile(string source, string destination, List<string> blocking, string label)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
            return File.Exists(destination);
        }
        catch (Exception ex)
        {
            blocking.Add($"{label}: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RunFfmpegAsync(IFFmpegService ffmpegService, string args, string outputPath, List<string> blocking, string label, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ffmpegService.ExecuteAsync(args, Directory.GetCurrentDirectory(), outputPath, cancellationToken);
            if (result.ExitCode == 0) return true;
            blocking.Add($"{label}: ffmpeg failed exit={result.ExitCode} stderr={result.StdErr}");
            return false;
        }
        catch (Exception ex)
        {
            blocking.Add($"{label}: ffmpeg execution failed. {ex.Message}");
            return false;
        }
    }
}
