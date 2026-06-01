using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
using NarrationEngineWeeklyNarrationSegment = Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine.WeeklyNarrationSegment;
using RenderingWeeklyRenderContract = Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.WeeklyRenderContract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration;

public interface IWeeklySkyForecastAudioGenerationService
{
    Task<WeeklySkyForecastAudioGenerationResponse> GenerateAsync(Guid pipelineRunId, WeeklySkyForecastAudioGenerationRequest request, CancellationToken cancellationToken);
}

public interface IWeeklySkyForecastTtsSynthesizer
{
    Task SynthesizeSsmlToFileAsync(string ssml, string outputPath, string voiceName, string audioFormat, CancellationToken cancellationToken);
}

public sealed record WeeklySkyForecastAudioGenerationRequest(
    bool GenerateLongform = true,
    bool GenerateShortform = true,
    bool OverwriteExisting = false,
    bool DryRun = false,
    string? VoiceName = "hi-IN-MadhurNeural",
    string? AudioFormat = "mp3",
    string? Language = null);

public sealed record WeeklySkyForecastAudioGenerationResponse(
    Guid PipelineRunId,
    bool DryRun,
    bool AudioGenerationReady,
    bool LongformAudioGenerated,
    bool ShortformAudioGenerated,
    string? LongformCombinedAudioPath,
    string? ShortformCombinedAudioPath,
    int LongformSegmentAudioCount,
    int ShortformSegmentAudioCount,
    int PlannedLongformSegmentAudioCount,
    int PlannedShortformSegmentAudioCount,
    string? PlannedLongformCombinedAudioPath,
    string? PlannedShortformCombinedAudioPath,
    string AudioGenerationReportPath,
    string AudioSegmentManifestPath,
    string AudioTimingValidationReportPath,
    double LongformActualAudioDurationSeconds,
    double ShortformActualAudioDurationSeconds,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string NormalizedLongformNarrationPath,
    string NormalizedShortformNarrationPath,
    bool NarrationParsingReady,
    string LongformNarrationSourceUsed,
    string ShortformNarrationSourceUsed,
    int LongformNormalizedSegmentCount,
    int ShortformNormalizedSegmentCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExistingLongformCombinedAudioPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExistingShortformCombinedAudioPath = null);

public sealed record WeeklyAudioGenerationReport(
    bool DryRun,
    bool TtsCalled,
    bool Mp3Generated,
    bool AudioConcatExecuted,
    bool AudioGenerationReady,
    int PlannedLongformSegmentAudioCount,
    int PlannedShortformSegmentAudioCount,
    bool LongformAudioGenerated,
    bool ShortformAudioGenerated,
    int LongformSegmentAudioCount,
    int ShortformSegmentAudioCount,
    string? LongformCombinedAudioPath,
    string? ShortformCombinedAudioPath,
    string? PlannedLongformCombinedAudioPath,
    string? PlannedShortformCombinedAudioPath,
    string VoiceName,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExistingLongformCombinedAudioPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExistingShortformCombinedAudioPath = null);

public sealed record WeeklyAudioSegmentManifest(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<WeeklyAudioSegmentManifestEntry> Longform,
    IReadOnlyList<WeeklyAudioSegmentManifestEntry> Shortform);

public sealed record WeeklyAudioSegmentManifestEntry(
    string SegmentId,
    string SegmentType,
    int ExpectedDurationSeconds,
    double ActualAudioDurationSeconds,
    string AudioPath,
    string VoiceName,
    double DurationDeltaSeconds,
    string Status);

public sealed record WeeklyAudioTimingValidationReport(
    int LongformExpectedDurationSeconds,
    double LongformActualAudioDurationSeconds,
    double LongformDurationDeltaSeconds,
    bool LongformTimingWithinTolerance,
    int ShortformExpectedDurationSeconds,
    double ShortformActualAudioDurationSeconds,
    double ShortformDurationDeltaSeconds,
    bool ShortformTimingWithinTolerance,
    IReadOnlyList<WeeklyAudioSegmentManifestEntry> SegmentsOutsideTolerance,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);


public sealed record WeeklyNormalizedNarrationPackage(
    Guid PipelineRunId,
    string Language,
    string EpisodeType,
    IReadOnlyList<WeeklyNormalizedNarrationSegment> Segments,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyNormalizedNarrationSegment(
    string SegmentId,
    string SegmentType,
    string NarrationText,
    int ExpectedDurationSeconds,
    int StartSecond,
    int EndSecond);

public sealed record WeeklyNarrationFileReaderResult(
    WeeklyNormalizedNarrationPackage Package,
    string SourceUsed,
    string NormalizedPath);

public sealed class WeeklySkyForecastAudioGenerationService(
    IOptions<RenderingOptions> renderingOptions,
    IOptions<AzureSpeechOptions> azureSpeechOptions,
    IWeeklySkyForecastTtsSynthesizer ttsSynthesizer,
    ILogger<WeeklySkyForecastAudioGenerationService> logger) : IWeeklySkyForecastAudioGenerationService
{
    private const double LongformSegmentTolerance = 0.15d;
    private const double ShortformSegmentTolerance = 0.20d;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private readonly RenderingOptions _renderingOptions = renderingOptions.Value;
    private readonly AzureSpeechOptions _azureSpeechOptions = azureSpeechOptions.Value;

    public async Task<WeeklySkyForecastAudioGenerationResponse> GenerateAsync(Guid pipelineRunId, WeeklySkyForecastAudioGenerationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("WEEKLY_AUDIO_GENERATION_START pipelineRunId={PipelineRunId} dryRun={DryRun} generateLongform={GenerateLongform} generateShortform={GenerateShortform}", pipelineRunId, request.DryRun, request.GenerateLongform, request.GenerateShortform);
        var warnings = new List<string>();
        var errors = new List<string>();
        try
        {
            var root = ResolveWorkingDirectoryRoot(pipelineRunId);
            var paths = WeeklyAudioRequiredPaths.FromRoot(root);
            CreateAudioDirectories(root);
            var loaded = await LoadInputsAsync(paths, pipelineRunId, request, warnings, cancellationToken);
            ValidateInputs(pipelineRunId, request, loaded, errors);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

            logger.LogInformation("WEEKLY_AUDIO_INPUTS_LOADED pipelineRunId={PipelineRunId} root={Root}", pipelineRunId, root);

            var voiceName = ResolveVoiceName(request.VoiceName, loaded.RenderContract.Language, loaded.Longform.Package.Language, loaded.Shortform.Package.Language);
            var audioFormat = string.IsNullOrWhiteSpace(request.AudioFormat) ? ResolveAudioFormat(_azureSpeechOptions.DefaultAudioFormat) : ResolveAudioFormat(request.AudioFormat);
            if (!string.Equals(audioFormat, "mp3", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Requested audioFormat '{request.AudioFormat}' is not supported in Phase 6.3. Using mp3.");
                audioFormat = "mp3";
            }

            if (request.DryRun)
            {
                logger.LogInformation("WEEKLY_AUDIO_DRY_RUN_START pipelineRunId={PipelineRunId}", pipelineRunId);
            }

            var longformEntries = request.GenerateLongform
                ? await GenerateSegmentsAsync(pipelineRunId, "longform", loaded.Longform.Package.Segments.Select(ToNarrationSegment).ToList(), loaded.AudioPlan.Segments, root, voiceName, audioFormat, request, warnings, cancellationToken)
                : [];
            var shortformEntries = request.GenerateShortform
                ? await GenerateSegmentsAsync(pipelineRunId, "shortform", loaded.Shortform.Package.Segments.Select(ToNarrationSegment).ToList(), loaded.AudioPlan.Segments, root, voiceName, audioFormat, request, warnings, cancellationToken)
                : [];

            var longformCombinedPath = Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3");
            var shortformCombinedPath = Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3");

            if (request.DryRun)
            {
                logger.LogInformation("WEEKLY_AUDIO_DRY_RUN_SSML_CREATED pipelineRunId={PipelineRunId} longformSsmlCount={LongformSsmlCount} shortformSsmlCount={ShortformSsmlCount}", pipelineRunId, longformEntries.Count, shortformEntries.Count);
                warnings.Add("Dry run completed. TTS was not called and MP3 files were not generated.");

                var dryRunReady = errors.Count == 0;
                var existingLongformCombinedPath = File.Exists(longformCombinedPath) ? longformCombinedPath : null;
                var existingShortformCombinedPath = File.Exists(shortformCombinedPath) ? shortformCombinedPath : null;
                var dryRunManifest = new WeeklyAudioSegmentManifest(pipelineRunId, DateTime.UtcNow, longformEntries, shortformEntries);
                var dryRunTimingReport = new WeeklyAudioTimingValidationReport(
                    loaded.RenderContract.Longform.DurationSeconds,
                    0,
                    0,
                    true,
                    loaded.RenderContract.Shortform.DurationSeconds,
                    0,
                    0,
                    true,
                    [],
                    warnings,
                    errors);
                var dryRunReport = new WeeklyAudioGenerationReport(
                    DryRun: true,
                    TtsCalled: false,
                    Mp3Generated: false,
                    AudioConcatExecuted: false,
                    AudioGenerationReady: dryRunReady,
                    PlannedLongformSegmentAudioCount: longformEntries.Count,
                    PlannedShortformSegmentAudioCount: shortformEntries.Count,
                    LongformAudioGenerated: false,
                    ShortformAudioGenerated: false,
                    LongformSegmentAudioCount: 0,
                    ShortformSegmentAudioCount: 0,
                    LongformCombinedAudioPath: null,
                    ShortformCombinedAudioPath: null,
                    PlannedLongformCombinedAudioPath: request.GenerateLongform ? longformCombinedPath : null,
                    PlannedShortformCombinedAudioPath: request.GenerateShortform ? shortformCombinedPath : null,
                    VoiceName: voiceName,
                    Warnings: warnings,
                    Errors: errors,
                    ExistingLongformCombinedAudioPath: existingLongformCombinedPath,
                    ExistingShortformCombinedAudioPath: existingShortformCombinedPath);

                await File.WriteAllTextAsync(paths.ManifestOutput, JsonSerializer.Serialize(dryRunManifest, JsonOptions), cancellationToken);
                await File.WriteAllTextAsync(paths.TimingReportOutput, JsonSerializer.Serialize(dryRunTimingReport, JsonOptions), cancellationToken);
                await File.WriteAllTextAsync(paths.GenerationReportOutput, JsonSerializer.Serialize(dryRunReport, JsonOptions), cancellationToken);

                logger.LogInformation("WEEKLY_AUDIO_DRY_RUN_COMPLETE pipelineRunId={PipelineRunId} ready={Ready}", pipelineRunId, dryRunReady);
                logger.LogInformation("WEEKLY_AUDIO_GENERATION_COMPLETE pipelineRunId={PipelineRunId} ready={Ready}", pipelineRunId, dryRunReady);
                return new WeeklySkyForecastAudioGenerationResponse(
                    PipelineRunId: pipelineRunId,
                    DryRun: true,
                    AudioGenerationReady: dryRunReady,
                    LongformAudioGenerated: false,
                    ShortformAudioGenerated: false,
                    LongformCombinedAudioPath: null,
                    ShortformCombinedAudioPath: null,
                    LongformSegmentAudioCount: 0,
                    ShortformSegmentAudioCount: 0,
                    PlannedLongformSegmentAudioCount: longformEntries.Count,
                    PlannedShortformSegmentAudioCount: shortformEntries.Count,
                    PlannedLongformCombinedAudioPath: request.GenerateLongform ? longformCombinedPath : null,
                    PlannedShortformCombinedAudioPath: request.GenerateShortform ? shortformCombinedPath : null,
                    AudioGenerationReportPath: paths.GenerationReportOutput,
                    AudioSegmentManifestPath: paths.ManifestOutput,
                    AudioTimingValidationReportPath: paths.TimingReportOutput,
                    LongformActualAudioDurationSeconds: 0,
                    ShortformActualAudioDurationSeconds: 0,
                    Warnings: warnings,
                    Errors: errors,
                    NormalizedLongformNarrationPath: loaded.Longform.NormalizedPath,
                    NormalizedShortformNarrationPath: loaded.Shortform.NormalizedPath,
                    NarrationParsingReady: errors.Count == 0,
                    LongformNarrationSourceUsed: loaded.Longform.SourceUsed,
                    ShortformNarrationSourceUsed: loaded.Shortform.SourceUsed,
                    LongformNormalizedSegmentCount: loaded.Longform.Package.Segments.Count,
                    ShortformNormalizedSegmentCount: loaded.Shortform.Package.Segments.Count,
                    ExistingLongformCombinedAudioPath: existingLongformCombinedPath,
                    ExistingShortformCombinedAudioPath: existingShortformCombinedPath);
            }

            var longformGenerated = false;
            var shortformGenerated = false;

            if (request.GenerateLongform && longformEntries.Count > 0)
            {
                logger.LogInformation("WEEKLY_AUDIO_COMBINE_START pipelineRunId={PipelineRunId} episodeType=longform", pipelineRunId);
                await CombineSegmentsAsync(longformEntries, longformCombinedPath, request.OverwriteExisting, cancellationToken);
                longformGenerated = File.Exists(longformCombinedPath);
                logger.LogInformation("WEEKLY_AUDIO_COMBINE_COMPLETE pipelineRunId={PipelineRunId} episodeType=longform outputPath={OutputPath}", pipelineRunId, longformCombinedPath);
            }
            if (request.GenerateShortform && shortformEntries.Count > 0)
            {
                logger.LogInformation("WEEKLY_AUDIO_COMBINE_START pipelineRunId={PipelineRunId} episodeType=shortform", pipelineRunId);
                await CombineSegmentsAsync(shortformEntries, shortformCombinedPath, request.OverwriteExisting, cancellationToken);
                shortformGenerated = File.Exists(shortformCombinedPath);
                logger.LogInformation("WEEKLY_AUDIO_COMBINE_COMPLETE pipelineRunId={PipelineRunId} episodeType=shortform outputPath={OutputPath}", pipelineRunId, shortformCombinedPath);
            }

            logger.LogInformation("WEEKLY_AUDIO_TIMING_VALIDATION_START pipelineRunId={PipelineRunId}", pipelineRunId);
            var longformActual = await ProbeDurationAsync(longformCombinedPath, cancellationToken);
            var shortformActual = await ProbeDurationAsync(shortformCombinedPath, cancellationToken);
            var outsideTolerance = longformEntries.Where(x => IsOutsideTolerance(x, LongformSegmentTolerance))
                .Concat(shortformEntries.Where(x => IsOutsideTolerance(x, ShortformSegmentTolerance)))
                .ToList();
            warnings.AddRange(outsideTolerance.Select(x => $"Audio segment {x.SegmentId} is outside timing tolerance. Expected={x.ExpectedDurationSeconds}s Actual={x.ActualAudioDurationSeconds:0.###}s."));
            var timingReport = new WeeklyAudioTimingValidationReport(
                loaded.RenderContract.Longform.DurationSeconds,
                longformActual,
                Round(longformActual - loaded.RenderContract.Longform.DurationSeconds),
                !request.GenerateLongform || IsWithinEpisodeTolerance(longformActual, loaded.RenderContract.Longform.DurationSeconds, LongformSegmentTolerance),
                loaded.RenderContract.Shortform.DurationSeconds,
                shortformActual,
                Round(shortformActual - loaded.RenderContract.Shortform.DurationSeconds),
                !request.GenerateShortform || IsWithinEpisodeTolerance(shortformActual, loaded.RenderContract.Shortform.DurationSeconds, ShortformSegmentTolerance),
                outsideTolerance,
                warnings,
                errors);
            logger.LogInformation("WEEKLY_AUDIO_TIMING_VALIDATION_COMPLETE pipelineRunId={PipelineRunId} outsideTolerance={OutsideTolerance}", pipelineRunId, outsideTolerance.Count);

            var manifest = new WeeklyAudioSegmentManifest(pipelineRunId, DateTime.UtcNow, longformEntries, shortformEntries);
            var ready = errors.Count == 0 && (!request.GenerateLongform || longformGenerated) && (!request.GenerateShortform || shortformGenerated);
            var longformSegmentAudioCount = longformEntries.Count(x => x.Status is "Generated" or "Existing");
            var shortformSegmentAudioCount = shortformEntries.Count(x => x.Status is "Generated" or "Existing");
            var report = new WeeklyAudioGenerationReport(
                DryRun: false,
                TtsCalled: true,
                Mp3Generated: true,
                AudioConcatExecuted: true,
                AudioGenerationReady: ready,
                PlannedLongformSegmentAudioCount: longformEntries.Count,
                PlannedShortformSegmentAudioCount: shortformEntries.Count,
                LongformAudioGenerated: longformGenerated,
                ShortformAudioGenerated: shortformGenerated,
                LongformSegmentAudioCount: longformSegmentAudioCount,
                ShortformSegmentAudioCount: shortformSegmentAudioCount,
                LongformCombinedAudioPath: longformCombinedPath,
                ShortformCombinedAudioPath: shortformCombinedPath,
                PlannedLongformCombinedAudioPath: request.GenerateLongform ? longformCombinedPath : null,
                PlannedShortformCombinedAudioPath: request.GenerateShortform ? shortformCombinedPath : null,
                VoiceName: voiceName,
                Warnings: warnings,
                Errors: errors);

            await File.WriteAllTextAsync(paths.ManifestOutput, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(paths.TimingReportOutput, JsonSerializer.Serialize(timingReport, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(paths.GenerationReportOutput, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);

            logger.LogInformation("WEEKLY_AUDIO_GENERATION_COMPLETE pipelineRunId={PipelineRunId} ready={Ready}", pipelineRunId, ready);
            return new WeeklySkyForecastAudioGenerationResponse(
                PipelineRunId: pipelineRunId,
                DryRun: false,
                AudioGenerationReady: ready,
                LongformAudioGenerated: longformGenerated,
                ShortformAudioGenerated: shortformGenerated,
                LongformCombinedAudioPath: longformCombinedPath,
                ShortformCombinedAudioPath: shortformCombinedPath,
                LongformSegmentAudioCount: longformSegmentAudioCount,
                ShortformSegmentAudioCount: shortformSegmentAudioCount,
                PlannedLongformSegmentAudioCount: longformEntries.Count,
                PlannedShortformSegmentAudioCount: shortformEntries.Count,
                PlannedLongformCombinedAudioPath: request.GenerateLongform ? longformCombinedPath : null,
                PlannedShortformCombinedAudioPath: request.GenerateShortform ? shortformCombinedPath : null,
                AudioGenerationReportPath: paths.GenerationReportOutput,
                AudioSegmentManifestPath: paths.ManifestOutput,
                AudioTimingValidationReportPath: paths.TimingReportOutput,
                LongformActualAudioDurationSeconds: longformActual,
                ShortformActualAudioDurationSeconds: shortformActual,
                Warnings: warnings,
                Errors: errors,
                NormalizedLongformNarrationPath: loaded.Longform.NormalizedPath,
                NormalizedShortformNarrationPath: loaded.Shortform.NormalizedPath,
                NarrationParsingReady: errors.Count == 0,
                LongformNarrationSourceUsed: loaded.Longform.SourceUsed,
                ShortformNarrationSourceUsed: loaded.Shortform.SourceUsed,
                LongformNormalizedSegmentCount: loaded.Longform.Package.Segments.Count,
                ShortformNormalizedSegmentCount: loaded.Shortform.Package.Segments.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WEEKLY_AUDIO_GENERATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
            throw;
        }
    }

    private async Task<List<WeeklyAudioSegmentManifestEntry>> GenerateSegmentsAsync(Guid pipelineRunId, string episodeType, IReadOnlyList<NarrationEngineWeeklyNarrationSegment> narrationSegments, IReadOnlyList<WeeklyAudioSegmentAlignment> alignments, string root, string voiceName, string audioFormat, WeeklySkyForecastAudioGenerationRequest request, List<string> warnings, CancellationToken cancellationToken)
    {
        var entries = new List<WeeklyAudioSegmentManifestEntry>();
        var alignmentById = alignments.Where(x => x.EpisodeType.Equals(episodeType, StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var segmentDirectory = Path.Combine(root, "audio", "segments", episodeType);
        Directory.CreateDirectory(segmentDirectory);
        foreach (var segment in narrationSegments)
        {
            var outputPath = Path.Combine(segmentDirectory, $"{SanitizeFileName(segment.SegmentId)}.mp3");
            var expected = alignmentById.TryGetValue(segment.SegmentId, out var alignment) ? alignment.DurationSeconds : segment.EstimatedDurationSeconds;
            var ssml = BuildSegmentSsml(segment.NarrationText, voiceName);
            var ssmlDirectory = Path.Combine(root, "audio", "temp", episodeType);
            Directory.CreateDirectory(ssmlDirectory);
            var ssmlPath = Path.Combine(ssmlDirectory, $"{SanitizeFileName(segment.SegmentId)}.ssml");
            await File.WriteAllTextAsync(ssmlPath, ssml, cancellationToken);

            if (request.DryRun)
            {
                entries.Add(new WeeklyAudioSegmentManifestEntry(segment.SegmentId, segment.SegmentType, expected, 0, outputPath, voiceName, 0, "Planned"));
                continue;
            }

            if (File.Exists(outputPath) && !request.OverwriteExisting)
            {
                var existingDuration = await ProbeDurationAsync(outputPath, cancellationToken);
                entries.Add(new WeeklyAudioSegmentManifestEntry(segment.SegmentId, segment.SegmentType, expected, existingDuration, outputPath, voiceName, Round(existingDuration - expected), "Existing"));
                continue;
            }

            logger.LogInformation("WEEKLY_AUDIO_SEGMENT_TTS_START pipelineRunId={PipelineRunId} episodeType={EpisodeType} segmentId={SegmentId}", pipelineRunId, episodeType, segment.SegmentId);
            var actualVoice = await SynthesizeSegmentWithFallbackAsync(ssml, outputPath, voiceName, audioFormat, cancellationToken);
            var duration = await ProbeDurationAsync(outputPath, cancellationToken);
            entries.Add(new WeeklyAudioSegmentManifestEntry(segment.SegmentId, segment.SegmentType, expected, duration, outputPath, actualVoice, Round(duration - expected), "Generated"));
            logger.LogInformation("WEEKLY_AUDIO_SEGMENT_TTS_COMPLETE pipelineRunId={PipelineRunId} episodeType={EpisodeType} segmentId={SegmentId} durationSeconds={DurationSeconds}", pipelineRunId, episodeType, segment.SegmentId, duration);
        }
        return entries;
    }

    private async Task<string> SynthesizeSegmentWithFallbackAsync(string ssml, string outputPath, string voiceName, string audioFormat, CancellationToken cancellationToken)
    {
        try
        {
            await ttsSynthesizer.SynthesizeSsmlToFileAsync(ssml, outputPath, voiceName, audioFormat, cancellationToken);
            return voiceName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !string.Equals(voiceName, ResolveFallbackVoice(voiceName), StringComparison.OrdinalIgnoreCase))
        {
            var fallbackVoice = ResolveFallbackVoice(voiceName);
            var fallbackSsml = ReplaceVoiceNameInSsml(ssml, voiceName, fallbackVoice);
            await ttsSynthesizer.SynthesizeSsmlToFileAsync(fallbackSsml, outputPath, fallbackVoice, audioFormat, cancellationToken);
            return fallbackVoice;
        }
    }

    private static string ResolveFallbackVoice(string voiceName)
    {
        if (voiceName.StartsWith("hi-", StringComparison.OrdinalIgnoreCase)) return "hi-IN-SwaraNeural";
        if (voiceName.StartsWith("en-IN", StringComparison.OrdinalIgnoreCase)) return "en-US-GuyNeural";
        if (voiceName.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return "en-US-GuyNeural";
        return voiceName;
    }

    private static string ReplaceVoiceNameInSsml(string ssml, string oldVoice, string newVoice)
        => ssml.Replace($"name=\"{SecurityElement.Escape(oldVoice)}\"", $"name=\"{SecurityElement.Escape(newVoice)}\"", StringComparison.OrdinalIgnoreCase);

    private async Task CombineSegmentsAsync(IReadOnlyList<WeeklyAudioSegmentManifestEntry> entries, string outputPath, bool overwrite, CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath) && !overwrite) return;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var concatPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"concat-{Path.GetFileNameWithoutExtension(outputPath)}.txt");
        var lines = entries.Select(e => $"file '{e.AudioPath.Replace('\\', '/').Replace("'", "'\\''")}'");
        await File.WriteAllLinesAsync(concatPath, lines, cancellationToken);
        var args = $"-y -f concat -safe 0 -i \"{concatPath}\" -c copy \"{outputPath}\"";
        var result = await RunProcessAsync(_renderingOptions.FfmpegPath, args, cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException($"FFmpeg concat failed for {outputPath}: {result.StandardError}");
    }

    private async Task<double> ProbeDurationAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return 0;
        var ffprobe = string.IsNullOrWhiteSpace(_renderingOptions.FfprobePath) ? "ffprobe" : _renderingOptions.FfprobePath!;
        var result = await RunProcessAsync(ffprobe, $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"", cancellationToken);
        return result.ExitCode == 0 && double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ? Round(duration) : 0;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await stdout, await stderr);
    }

    private static string ResolveAudioFormat(string? requestedFormat)
    {
        if (string.IsNullOrWhiteSpace(requestedFormat)) return "mp3";
        var normalized = requestedFormat.Trim().TrimStart('.').ToLowerInvariant();
        return normalized.Contains("mp3", StringComparison.OrdinalIgnoreCase) ? "mp3" : normalized;
    }

    private string ResolveWorkingDirectoryRoot(Guid pipelineRunId)
    {
        var workingRoot = string.IsNullOrWhiteSpace(_renderingOptions.WorkingDirectory) ? "./media-output" : _renderingOptions.WorkingDirectory;
        if (!Directory.Exists(workingRoot)) throw new DirectoryNotFoundException($"Pipeline working directory root does not exist: {workingRoot}");
        var matches = Directory.EnumerateDirectories(workingRoot, pipelineRunId.ToString("N"), SearchOption.AllDirectories)
            .Concat(Directory.EnumerateDirectories(workingRoot, pipelineRunId.ToString("D"), SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => File.Exists(Path.Combine(path, "render", "weekly-render-contract.json")) && File.Exists(Path.Combine(path, "render", "audio-alignment-plan.json")))
            .ToList();
        return matches.Count switch { 0 => throw new DirectoryNotFoundException($"No WeeklySkyForecast workingDirectoryRoot was found for pipelineRunId {pipelineRunId} under {workingRoot}."), 1 => matches[0], _ => matches.OrderByDescending(Directory.GetLastWriteTimeUtc).First() };
    }

    private static async Task<WeeklyAudioLoadedInputs> LoadInputsAsync(WeeklyAudioRequiredPaths paths, Guid pipelineRunId, WeeklySkyForecastAudioGenerationRequest request, List<string> warnings, CancellationToken cancellationToken)
    {
        foreach (var path in paths.RequiredInputs)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Required audio input file is missing: {path}", path);
        }

        var renderContract = await ReadJsonAsync<RenderingWeeklyRenderContract>(paths.RenderContract, cancellationToken);
        var audioPlan = await ReadJsonAsync<WeeklyAudioAlignmentPlan>(paths.AudioAlignmentPlan, cancellationToken);
        var productionManifestLanguage = await TryReadLanguageAsync(paths.ProductionAssetManifest, cancellationToken);

        var reader = new WeeklyNarrationFileReader(paths, pipelineRunId, request.Language, renderContract.Language, productionManifestLanguage);
        var longform = await reader.ReadAsync("longform", "LongFormWeeklyForecast", cancellationToken);
        var shortform = await reader.ReadAsync("shortform", "ShortFormWeeklyForecast", cancellationToken);
        warnings.AddRange(longform.Package.Warnings);
        warnings.AddRange(shortform.Package.Warnings);

        return new WeeklyAudioLoadedInputs(
            longform,
            shortform,
            await ReadJsonAsync<object>(paths.NarrationTimelineMap, cancellationToken),
            audioPlan,
            renderContract);
    }

    private static async Task<string?> TryReadLanguageAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            return WeeklyNarrationFileReader.TryGetString(document.RootElement, "language");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NarrationEngineWeeklyNarrationSegment ToNarrationSegment(WeeklyNormalizedNarrationSegment segment)
        => new(segment.SegmentId, segment.SegmentType, segment.NarrationText, segment.ExpectedDurationSeconds, 1, 1, false);

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
        => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions) ?? throw new InvalidOperationException($"Unable to deserialize required audio input file: {path}");

    private static void ValidateInputs(Guid pipelineRunId, WeeklySkyForecastAudioGenerationRequest request, WeeklyAudioLoadedInputs loaded, List<string> errors)
    {
        if (pipelineRunId == Guid.Empty) errors.Add("pipelineRunId is required.");
        if (!request.GenerateLongform && !request.GenerateShortform) errors.Add("At least one of generateLongform or generateShortform must be true.");

        ValidateNarrationPackage("Longform", loaded.Longform.Package, pipelineRunId, request.GenerateLongform, errors);
        ValidateNarrationPackage("Shortform", loaded.Shortform.Package, pipelineRunId, request.GenerateShortform, errors);
        ValidateAudioPlan(loaded.AudioPlan, pipelineRunId, errors);
        ValidateRenderContract(loaded.RenderContract, pipelineRunId, errors);
    }

    private static void ValidateNarrationPackage(string label, WeeklyNormalizedNarrationPackage package, Guid pipelineRunId, bool requested, List<string> errors)
    {
        if (package.PipelineRunId != pipelineRunId) errors.Add($"{label} narration pipelineRunId {package.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (string.IsNullOrWhiteSpace(package.Language)) errors.Add($"{label} narration language is missing.");

        if (package.Segments is null)
        {
            errors.Add($"{label} narration segments are missing.");
            return;
        }

        if (requested && package.Segments.Count == 0) errors.Add($"{label} narration has no segments.");
        foreach (var (segment, index) in package.Segments.Select((segment, index) => (segment, index)))
        {
            if (segment is null)
            {
                errors.Add($"{label} narration segment at index {index} is missing.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(segment.SegmentId)) errors.Add($"{label} narration segment at index {index} is missing segmentId.");
            if (string.IsNullOrWhiteSpace(segment.SegmentType)) errors.Add($"{label} narration segment {segment.SegmentId} is missing segmentType.");
            if (string.IsNullOrWhiteSpace(segment.NarrationText)) errors.Add($"{label} narration segment {segment.SegmentId} is missing narrationText.");
        }
    }

    private static void ValidateAudioPlan(WeeklyAudioAlignmentPlan audioPlan, Guid pipelineRunId, List<string> errors)
    {
        if (audioPlan.PipelineRunId != pipelineRunId) errors.Add($"Audio alignment plan pipelineRunId {audioPlan.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (audioPlan.Segments is null)
        {
            errors.Add("Audio alignment plan segments are missing.");
            return;
        }

        foreach (var (segment, index) in audioPlan.Segments.Select((segment, index) => (segment, index)))
        {
            if (segment is null)
            {
                errors.Add($"Audio alignment plan segment at index {index} is missing.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(segment.EpisodeType)) errors.Add($"Audio alignment plan segment at index {index} is missing episodeType.");
            if (string.IsNullOrWhiteSpace(segment.SegmentId)) errors.Add($"Audio alignment plan segment at index {index} is missing segmentId.");
        }
    }

    private static void ValidateRenderContract(RenderingWeeklyRenderContract renderContract, Guid pipelineRunId, List<string> errors)
    {
        if (renderContract.PipelineRunId != pipelineRunId) errors.Add($"Render contract pipelineRunId {renderContract.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (string.IsNullOrWhiteSpace(renderContract.Language)) errors.Add("Render contract language is missing.");
        if (renderContract.Longform is null) errors.Add("Render contract longform contract is missing.");
        if (renderContract.Shortform is null) errors.Add("Render contract shortform contract is missing.");
    }

    private static void CreateAudioDirectories(string root)
    {
        foreach (var relative in new[] { "audio", "audio/longform", "audio/shortform", "audio/segments", "audio/segments/longform", "audio/segments/shortform", "audio/logs", "audio/temp", "audio/temp/longform", "audio/temp/shortform" })
        {
            Directory.CreateDirectory(Path.Combine(root, relative));
        }
    }

    private string ResolveVoiceName(string? requestedVoice, params string?[] languages)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice)) return requestedVoice.Trim();
        var isHindi = languages.Any(language => language?.StartsWith("hi", StringComparison.OrdinalIgnoreCase) == true);
        if (isHindi) return FirstConfigured(_azureSpeechOptions.DefaultVoiceName, _azureSpeechOptions.Voices.GetValueOrDefault("hi"), "hi-IN-MadhurNeural");
        return FirstConfigured(_azureSpeechOptions.Voices.GetValueOrDefault("en"), _azureSpeechOptions.PrimaryVoice, "en-IN-PrabhatNeural");
    }

    private static string FirstConfigured(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string BuildSegmentSsml(string text, string voiceName)
    {
        var language = voiceName.StartsWith("hi-IN", StringComparison.OrdinalIgnoreCase) ? "hi-IN" : voiceName.StartsWith("en-IN", StringComparison.OrdinalIgnoreCase) ? "en-IN" : "en-US";
        var escapedVoice = SecurityElement.Escape(voiceName) ?? "hi-IN-MadhurNeural";
        var body = BuildSsmlBody(text);
        return $"""
               <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="{language}">
                 <voice name="{escapedVoice}">
                   <prosody rate="-5%">{body}</prosody>
                 </voice>
               </speak>
               """;
    }

    private static string BuildSsmlBody(string text)
    {
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split("\n\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" <break time=\"500ms\"/> ", paragraphs.Select(paragraph => AddSentencePauses(SecurityElement.Escape(string.Join(' ', paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))) ?? string.Empty)));
    }

    private static string AddSentencePauses(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            builder.Append(ch);
            if (ch is '.' or '?' or '!' or '।') builder.Append("<break time=\"300ms\"/> ");
        }
        return builder.ToString().Trim();
    }

    private static bool IsOutsideTolerance(WeeklyAudioSegmentManifestEntry entry, double tolerance)
        => entry.ExpectedDurationSeconds > 0 && Math.Abs(entry.DurationDeltaSeconds) > entry.ExpectedDurationSeconds * tolerance;

    private static bool IsWithinEpisodeTolerance(double actual, int expected, double tolerance)
        => expected <= 0 || Math.Abs(actual - expected) <= expected * tolerance;

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static string SanitizeFileName(string value) => string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();

    private sealed class WeeklyNarrationFileReader(WeeklyAudioRequiredPaths paths, Guid requestedPipelineRunId, string? requestLanguage, string? renderContractLanguage, string? productionManifestLanguage)
    {
        public async Task<WeeklyNarrationFileReaderResult> ReadAsync(string episodeKey, string normalizedEpisodeType, CancellationToken cancellationToken)
        {
            var primaryPath = string.Equals(episodeKey, "shortform", StringComparison.OrdinalIgnoreCase) ? paths.ShortformNarration : paths.LongformNarration;
            var normalizedPath = string.Equals(episodeKey, "shortform", StringComparison.OrdinalIgnoreCase) ? paths.NormalizedShortformNarration : paths.NormalizedLongformNarration;
            var attempts = new[]
            {
                (Path: primaryPath, Source: Path.GetFileName(primaryPath), Kind: "narration"),
                (Path: paths.NarrationTimelineMap, Source: Path.GetFileName(paths.NarrationTimelineMap), Kind: "timelineMap"),
                (Path: paths.FinalRenderTimeline, Source: Path.GetFileName(paths.FinalRenderTimeline), Kind: "finalTimeline")
            };

            var failureReasons = new List<string>();
            foreach (var attempt in attempts)
            {
                if (!File.Exists(attempt.Path))
                {
                    failureReasons.Add($"{attempt.Source} is missing.");
                    continue;
                }

                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(attempt.Path, cancellationToken));
                var warnings = new List<string>();
                var pipelineRunId = ResolvePipelineRunId(document.RootElement, attempt.Source, warnings);
                var language = ResolveLanguage(document.RootElement, attempt.Source, warnings);
                var segments = ExtractSegments(document.RootElement, episodeKey, attempt.Kind);
                if (segments.Count == 0)
                {
                    failureReasons.Add($"{attempt.Source} did not contain usable {episodeKey} narration segments with narrationText.");
                    continue;
                }

                var package = new WeeklyNormalizedNarrationPackage(pipelineRunId, language, normalizedEpisodeType, segments, warnings);
                await File.WriteAllTextAsync(normalizedPath, JsonSerializer.Serialize(package, JsonOptions), cancellationToken);
                return new WeeklyNarrationFileReaderResult(package, attempt.Source, normalizedPath);
            }

            throw new InvalidOperationException($"Unable to read {episodeKey} narration. Tried {string.Join(" -> ", attempts.Select(x => x.Source))}. {string.Join(" ", failureReasons)}");
        }

        private Guid ResolvePipelineRunId(JsonElement root, string source, List<string> warnings)
        {
            var raw = TryGetString(root, "pipelineRunId");
            if (Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty) return parsed;
            warnings.Add($"{source} is missing pipelineRunId; using requested pipelineRunId {requestedPipelineRunId}.");
            return requestedPipelineRunId;
        }

        private string ResolveLanguage(JsonElement root, string source, List<string> warnings)
        {
            var language = FirstNonEmpty(TryGetString(root, "language"), requestLanguage, renderContractLanguage, productionManifestLanguage, "hi")!;
            if (string.IsNullOrWhiteSpace(TryGetString(root, "language"))) warnings.Add($"{source} is missing language; using inferred language '{language}'.");
            return language;
        }

        private static List<WeeklyNormalizedNarrationSegment> ExtractSegments(JsonElement root, string episodeKey, string kind)
        {
            var segmentsRoot = LocateSegmentsRoot(root, episodeKey, kind);
            if (segmentsRoot is null || segmentsRoot.Value.ValueKind != JsonValueKind.Array) return [];

            var result = new List<WeeklyNormalizedNarrationSegment>();
            foreach (var item in segmentsRoot.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var itemEpisode = TryGetString(item, "episodeType");
                if (!string.IsNullOrWhiteSpace(itemEpisode) && !EpisodeMatches(itemEpisode, episodeKey)) continue;

                var segmentId = FirstNonEmpty(TryGetString(item, "segmentId"), TryGetString(item, "id"), TryGetString(item, "segmentCode"));
                var narrationText = FirstNonEmpty(TryGetString(item, "narrationText"), TryGetString(item, "text"), TryGetString(item, "script"));
                if (string.IsNullOrWhiteSpace(segmentId) || string.IsNullOrWhiteSpace(narrationText)) continue;

                var segmentType = FirstNonEmpty(TryGetString(item, "segmentType"), TryGetString(item, "type"), "NarrationSegment")!;
                var expectedDuration = FirstPositive(TryGetInt(item, "expectedDurationSeconds"), TryGetInt(item, "estimatedDurationSeconds"), TryGetInt(item, "durationSeconds"), Difference(TryGetInt(item, "startSecond"), TryGetInt(item, "endSecond")), Difference(TryGetInt(item, "narrationStart"), TryGetInt(item, "narrationEnd")));
                var startSecond = FirstNonNegative(TryGetInt(item, "startSecond"), TryGetInt(item, "narrationStart"));
                var endSecond = FirstNonNegative(TryGetInt(item, "endSecond"), TryGetInt(item, "narrationEnd"));
                if (endSecond <= 0 && expectedDuration > 0) endSecond = startSecond + expectedDuration;

                result.Add(new WeeklyNormalizedNarrationSegment(segmentId.Trim(), segmentType.Trim(), narrationText.Trim(), expectedDuration, startSecond, endSecond));
            }
            return result;
        }

        private static JsonElement? LocateSegmentsRoot(JsonElement root, string episodeKey, string kind)
        {
            if (root.ValueKind == JsonValueKind.Array) return root;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (string.Equals(kind, "finalTimeline", StringComparison.OrdinalIgnoreCase) || HasProperty(root, episodeKey))
            {
                if (TryGetProperty(root, episodeKey, out var episode) && TryGetProperty(episode, "segments", out var episodeSegments)) return episodeSegments;
            }

            if (TryGetProperty(root, "segments", out var segments)) return segments;
            if (TryGetProperty(root, "narrationSegments", out var narrationSegments)) return narrationSegments;
            return null;
        }

        private static bool EpisodeMatches(string value, string episodeKey)
        {
            if (string.Equals(value, episodeKey, StringComparison.OrdinalIgnoreCase)) return true;
            return episodeKey.Equals("longform", StringComparison.OrdinalIgnoreCase)
                ? value.Contains("long", StringComparison.OrdinalIgnoreCase)
                : value.Contains("short", StringComparison.OrdinalIgnoreCase);
        }

        public static string? TryGetString(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out var property)) return null;
            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static int? TryGetInt(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out var property)) return null;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)) return value;
            return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : null;
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
        {
            property = default;
            if (element.ValueKind != JsonValueKind.Object) return false;
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
            return false;
        }

        private static bool HasProperty(JsonElement element, string propertyName) => TryGetProperty(element, propertyName, out _);
        private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        private static int FirstPositive(params int?[] values) => values.FirstOrDefault(value => value.GetValueOrDefault() > 0) ?? 0;
        private static int FirstNonNegative(params int?[] values) => values.FirstOrDefault(value => value.HasValue && value.Value >= 0) ?? 0;
        private static int? Difference(int? start, int? end) => start.HasValue && end.HasValue && end.Value > start.Value ? end.Value - start.Value : null;
    }

    private sealed record WeeklyAudioLoadedInputs(WeeklyNarrationFileReaderResult Longform, WeeklyNarrationFileReaderResult Shortform, object _NarrationTimelineMap, WeeklyAudioAlignmentPlan AudioPlan, RenderingWeeklyRenderContract RenderContract);

    private sealed record WeeklyAudioRequiredPaths(string Root, string LongformNarration, string ShortformNarration, string NarrationTimelineMap, string AudioAlignmentPlan, string RenderContract, string FinalRenderTimeline, string ProductionAssetManifest, string NormalizedLongformNarration, string NormalizedShortformNarration, string GenerationReportOutput, string ManifestOutput, string TimingReportOutput)
    {
        public IReadOnlyList<string> RequiredInputs => [LongformNarration, ShortformNarration, NarrationTimelineMap, AudioAlignmentPlan, RenderContract];
        public static WeeklyAudioRequiredPaths FromRoot(string root) => new(
            root,
            Path.Combine(root, "episode", "longform-narration.json"),
            Path.Combine(root, "episode", "shortform-narration.json"),
            Path.Combine(root, "episode", "narration-timeline-map.json"),
            Path.Combine(root, "render", "audio-alignment-plan.json"),
            Path.Combine(root, "render", "weekly-render-contract.json"),
            Path.Combine(root, "episode", "final-render-timeline.json"),
            Path.Combine(root, "episode", "weekly-production-asset-manifest.json"),
            Path.Combine(root, "audio", "normalized-longform-narration.json"),
            Path.Combine(root, "audio", "normalized-shortform-narration.json"),
            Path.Combine(root, "audio", "audio-generation-report.json"),
            Path.Combine(root, "audio", "audio-segment-manifest.json"),
            Path.Combine(root, "audio", "audio-timing-validation-report.json"));
    }
}
