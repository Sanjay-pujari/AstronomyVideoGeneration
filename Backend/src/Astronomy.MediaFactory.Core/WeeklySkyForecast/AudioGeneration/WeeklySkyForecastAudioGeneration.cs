using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
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
    string? AudioFormat = "mp3");

public sealed record WeeklySkyForecastAudioGenerationResponse(
    Guid PipelineRunId,
    bool AudioGenerationReady,
    bool LongformAudioGenerated,
    bool ShortformAudioGenerated,
    string LongformCombinedAudioPath,
    string ShortformCombinedAudioPath,
    int LongformSegmentAudioCount,
    int ShortformSegmentAudioCount,
    string AudioGenerationReportPath,
    string AudioSegmentManifestPath,
    string AudioTimingValidationReportPath,
    double LongformActualAudioDurationSeconds,
    double ShortformActualAudioDurationSeconds,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyAudioGenerationReport(
    bool AudioGenerationReady,
    bool LongformAudioGenerated,
    bool ShortformAudioGenerated,
    int LongformSegmentAudioCount,
    int ShortformSegmentAudioCount,
    string LongformCombinedAudioPath,
    string ShortformCombinedAudioPath,
    string VoiceName,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

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
            var loaded = await LoadInputsAsync(paths, cancellationToken);
            ValidateInputs(pipelineRunId, request, loaded, errors);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

            CreateAudioDirectories(root);
            logger.LogInformation("WEEKLY_AUDIO_INPUTS_LOADED pipelineRunId={PipelineRunId} root={Root}", pipelineRunId, root);

            var voiceName = ResolveVoiceName(request.VoiceName, loaded.RenderContract.Language, loaded.Longform.Language, loaded.Shortform.Language);
            var audioFormat = string.IsNullOrWhiteSpace(request.AudioFormat) ? ResolveAudioFormat(_azureSpeechOptions.DefaultAudioFormat) : ResolveAudioFormat(request.AudioFormat);
            if (!string.Equals(audioFormat, "mp3", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Requested audioFormat '{request.AudioFormat}' is not supported in Phase 6.3. Using mp3.");
                audioFormat = "mp3";
            }

            var longformEntries = request.GenerateLongform
                ? await GenerateSegmentsAsync(pipelineRunId, "longform", loaded.Longform.Segments, loaded.AudioPlan.Segments, root, voiceName, audioFormat, request, warnings, cancellationToken)
                : [];
            var shortformEntries = request.GenerateShortform
                ? await GenerateSegmentsAsync(pipelineRunId, "shortform", loaded.Shortform.Segments, loaded.AudioPlan.Segments, root, voiceName, audioFormat, request, warnings, cancellationToken)
                : [];

            var longformCombinedPath = Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3");
            var shortformCombinedPath = Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3");
            var longformGenerated = request.DryRun && request.GenerateLongform && longformEntries.Count > 0;
            var shortformGenerated = request.DryRun && request.GenerateShortform && shortformEntries.Count > 0;

            if (!request.DryRun)
            {
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
            }

            logger.LogInformation("WEEKLY_AUDIO_TIMING_VALIDATION_START pipelineRunId={PipelineRunId}", pipelineRunId);
            var longformActual = request.DryRun ? 0 : await ProbeDurationAsync(longformCombinedPath, cancellationToken);
            var shortformActual = request.DryRun ? 0 : await ProbeDurationAsync(shortformCombinedPath, cancellationToken);
            var outsideTolerance = longformEntries.Where(x => IsOutsideTolerance(x, LongformSegmentTolerance))
                .Concat(shortformEntries.Where(x => IsOutsideTolerance(x, ShortformSegmentTolerance)))
                .ToList();
            warnings.AddRange(outsideTolerance.Select(x => $"Audio segment {x.SegmentId} is outside timing tolerance. Expected={x.ExpectedDurationSeconds}s Actual={x.ActualAudioDurationSeconds:0.###}s."));
            var timingReport = new WeeklyAudioTimingValidationReport(
                loaded.RenderContract.Longform.DurationSeconds,
                longformActual,
                Round(longformActual - loaded.RenderContract.Longform.DurationSeconds),
                !request.GenerateLongform || request.DryRun || IsWithinEpisodeTolerance(longformActual, loaded.RenderContract.Longform.DurationSeconds, LongformSegmentTolerance),
                loaded.RenderContract.Shortform.DurationSeconds,
                shortformActual,
                Round(shortformActual - loaded.RenderContract.Shortform.DurationSeconds),
                !request.GenerateShortform || request.DryRun || IsWithinEpisodeTolerance(shortformActual, loaded.RenderContract.Shortform.DurationSeconds, ShortformSegmentTolerance),
                outsideTolerance,
                warnings,
                errors);
            logger.LogInformation("WEEKLY_AUDIO_TIMING_VALIDATION_COMPLETE pipelineRunId={PipelineRunId} outsideTolerance={OutsideTolerance}", pipelineRunId, outsideTolerance.Count);

            var manifest = new WeeklyAudioSegmentManifest(pipelineRunId, DateTime.UtcNow, longformEntries, shortformEntries);
            var ready = errors.Count == 0 && (request.DryRun || ((!request.GenerateLongform || longformGenerated) && (!request.GenerateShortform || shortformGenerated)));
            var report = new WeeklyAudioGenerationReport(ready, longformGenerated, shortformGenerated, longformEntries.Count(x => x.Status is "Generated" or "Existing" or "Planned"), shortformEntries.Count(x => x.Status is "Generated" or "Existing" or "Planned"), longformCombinedPath, shortformCombinedPath, voiceName, warnings, errors);

            await File.WriteAllTextAsync(paths.ManifestOutput, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(paths.TimingReportOutput, JsonSerializer.Serialize(timingReport, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(paths.GenerationReportOutput, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);

            logger.LogInformation("WEEKLY_AUDIO_GENERATION_COMPLETE pipelineRunId={PipelineRunId} ready={Ready}", pipelineRunId, ready);
            return new WeeklySkyForecastAudioGenerationResponse(pipelineRunId, ready, longformGenerated, shortformGenerated, longformCombinedPath, shortformCombinedPath, report.LongformSegmentAudioCount, report.ShortformSegmentAudioCount, paths.GenerationReportOutput, paths.ManifestOutput, paths.TimingReportOutput, longformActual, shortformActual, warnings, errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WEEKLY_AUDIO_GENERATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
            throw;
        }
    }

    private async Task<List<WeeklyAudioSegmentManifestEntry>> GenerateSegmentsAsync(Guid pipelineRunId, string episodeType, IReadOnlyList<WeeklyNarrationSegment> narrationSegments, IReadOnlyList<WeeklyAudioSegmentAlignment> alignments, string root, string voiceName, string audioFormat, WeeklySkyForecastAudioGenerationRequest request, List<string> warnings, CancellationToken cancellationToken)
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
            var ssmlPath = Path.Combine(root, "audio", "temp", $"{episodeType}-{SanitizeFileName(segment.SegmentId)}.ssml");
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

    private static async Task<WeeklyAudioLoadedInputs> LoadInputsAsync(WeeklyAudioRequiredPaths paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths.RequiredInputs)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Required audio input file is missing: {path}", path);
        }
        return new WeeklyAudioLoadedInputs(
            await ReadJsonAsync<WeeklyNarrationPackage>(paths.LongformNarration, cancellationToken),
            await ReadJsonAsync<WeeklyNarrationPackage>(paths.ShortformNarration, cancellationToken),
            await ReadJsonAsync<object>(paths.NarrationTimelineMap, cancellationToken),
            await ReadJsonAsync<WeeklyAudioAlignmentPlan>(paths.AudioAlignmentPlan, cancellationToken),
            await ReadJsonAsync<WeeklyRenderContract>(paths.RenderContract, cancellationToken));
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
        => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions) ?? throw new InvalidOperationException($"Unable to deserialize required audio input file: {path}");

    private static void ValidateInputs(Guid pipelineRunId, WeeklySkyForecastAudioGenerationRequest request, WeeklyAudioLoadedInputs loaded, List<string> errors)
    {
        if (pipelineRunId == Guid.Empty) errors.Add("pipelineRunId is required.");
        if (!request.GenerateLongform && !request.GenerateShortform) errors.Add("At least one of generateLongform or generateShortform must be true.");
        if (loaded.Longform.PipelineRunId != pipelineRunId) errors.Add($"Longform narration pipelineRunId {loaded.Longform.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (loaded.Shortform.PipelineRunId != pipelineRunId) errors.Add($"Shortform narration pipelineRunId {loaded.Shortform.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (loaded.AudioPlan.PipelineRunId != pipelineRunId) errors.Add($"Audio alignment plan pipelineRunId {loaded.AudioPlan.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (loaded.RenderContract.PipelineRunId != pipelineRunId) errors.Add($"Render contract pipelineRunId {loaded.RenderContract.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (request.GenerateLongform && loaded.Longform.Segments.Count == 0) errors.Add("Longform narration has no segments.");
        if (request.GenerateShortform && loaded.Shortform.Segments.Count == 0) errors.Add("Shortform narration has no segments.");
    }

    private static void CreateAudioDirectories(string root)
    {
        foreach (var relative in new[] { "audio", "audio/longform", "audio/shortform", "audio/segments", "audio/segments/longform", "audio/segments/shortform", "audio/logs", "audio/temp" })
        {
            Directory.CreateDirectory(Path.Combine(root, relative));
        }
    }

    private string ResolveVoiceName(string? requestedVoice, params string[] languages)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice)) return requestedVoice.Trim();
        var isHindi = languages.Any(language => language.StartsWith("hi", StringComparison.OrdinalIgnoreCase));
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

    private sealed record WeeklyAudioLoadedInputs(WeeklyNarrationPackage Longform, WeeklyNarrationPackage Shortform, object _NarrationTimelineMap, WeeklyAudioAlignmentPlan AudioPlan, WeeklyRenderContract RenderContract);

    private sealed record WeeklyAudioRequiredPaths(string Root, string LongformNarration, string ShortformNarration, string NarrationTimelineMap, string AudioAlignmentPlan, string RenderContract, string GenerationReportOutput, string ManifestOutput, string TimingReportOutput)
    {
        public IReadOnlyList<string> RequiredInputs => [LongformNarration, ShortformNarration, NarrationTimelineMap, AudioAlignmentPlan, RenderContract];
        public static WeeklyAudioRequiredPaths FromRoot(string root) => new(
            root,
            Path.Combine(root, "episode", "longform-narration.json"),
            Path.Combine(root, "episode", "shortform-narration.json"),
            Path.Combine(root, "episode", "narration-timeline-map.json"),
            Path.Combine(root, "render", "audio-alignment-plan.json"),
            Path.Combine(root, "render", "weekly-render-contract.json"),
            Path.Combine(root, "audio", "audio-generation-report.json"),
            Path.Combine(root, "audio", "audio-segment-manifest.json"),
            Path.Combine(root, "audio", "audio-timing-validation-report.json"));
    }
}
