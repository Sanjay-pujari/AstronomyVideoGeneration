using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;

public interface IWeeklyExistingRunVideoRenderer
{
    Task<WeeklyExistingRunRenderResponse> RenderAsync(Guid pipelineRunId, WeeklyExistingRunRenderRequest request, CancellationToken cancellationToken);
}

public sealed record WeeklyExistingRunRenderRequest(
    bool RenderLongform = true,
    bool RenderShortform = true,
    bool OverwriteExisting = false,
    bool DryRun = false);

public sealed record WeeklyExistingRunRenderResponse(
    Guid PipelineRunId,
    bool RenderVideoReady,
    bool DryRun,
    bool LongformRequested,
    bool LongformRendered,
    bool LongformSkipped,
    string LongformVideoPath,
    bool ShortformRequested,
    bool ShortformRendered,
    bool ShortformSkipped,
    string ShortformVideoPath,
    string VideoRenderReportPath,
    string FfmpegExecutionReportPath,
    IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> PlannedCommands,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyExistingRunVideoRenderReport(
    Guid PipelineRunId,
    DateTime RenderStartedAtUtc,
    DateTime RenderCompletedAtUtc,
    bool DryRun,
    WeeklyExistingRunEpisodeRenderReport Longform,
    WeeklyExistingRunEpisodeRenderReport Shortform,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyExistingRunEpisodeRenderReport(
    bool Requested,
    bool Rendered,
    bool Skipped,
    string OutputPath,
    int DurationSeconds,
    long FileSizeBytes,
    bool AudioAttached);

public sealed record WeeklyExistingRunFfmpegExecutionReport(
    Guid PipelineRunId,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    bool DryRun,
    IReadOnlyList<WeeklyExistingRunFfmpegCommandReport> Commands,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyExistingRunFfmpegCommandReport(
    string EpisodeType,
    string OutputPath,
    bool Planned,
    bool Executed,
    bool Skipped,
    int? ExitCode,
    long ElapsedMilliseconds,
    string Command,
    string? StandardError,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyExistingRunFfmpegCommandPlan(
    string EpisodeType,
    string OutputPath,
    string ConcatFilePath,
    string? AudioPath,
    bool AudioAttached,
    string Command,
    IReadOnlyList<string> SegmentFiles);

public sealed class WeeklyExistingRunVideoRenderer(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<WeeklyExistingRunVideoRenderer> logger) : IWeeklyExistingRunVideoRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private readonly RenderingOptions _renderingOptions = renderingOptions.Value;

    public async Task<WeeklyExistingRunRenderResponse> RenderAsync(Guid pipelineRunId, WeeklyExistingRunRenderRequest request, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        logger.LogInformation("WEEKLY_RENDER_EXISTING_RUN_START pipelineRunId={PipelineRunId} dryRun={DryRun} renderLongform={RenderLongform} renderShortform={RenderShortform}", pipelineRunId, request.DryRun, request.RenderLongform, request.RenderShortform);

        var warnings = new List<string>();
        var errors = new List<string>();
        var commandReports = new List<WeeklyExistingRunFfmpegCommandReport>();
        var commandPlans = new List<WeeklyExistingRunFfmpegCommandPlan>();

        try
        {
            var root = ResolveWorkingDirectoryRoot(pipelineRunId);
            var renderDirectory = Path.Combine(root, "render");
            var paths = WeeklyExistingRunRequiredPaths.FromRoot(root);
            var loaded = await LoadInputsAsync(paths, cancellationToken);
            logger.LogInformation("WEEKLY_RENDER_INPUTS_LOADED pipelineRunId={PipelineRunId} root={Root}", pipelineRunId, root);

            logger.LogInformation("WEEKLY_RENDER_VALIDATION_START pipelineRunId={PipelineRunId}", pipelineRunId);
            ValidateInputs(pipelineRunId, root, request, loaded, errors);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(" ", errors));
            }

            Directory.CreateDirectory(Path.Combine(renderDirectory, "longform"));
            Directory.CreateDirectory(Path.Combine(renderDirectory, "shortform"));
            Directory.CreateDirectory(Path.Combine(renderDirectory, "logs"));
            Directory.CreateDirectory(Path.Combine(renderDirectory, "temp"));
            logger.LogInformation("WEEKLY_RENDER_VALIDATION_PASSED pipelineRunId={PipelineRunId}", pipelineRunId);

            var longformOutput = NormalizeOutputPath(loaded.Contract.Longform.OutputPath, Path.Combine(renderDirectory, "longform", "weekly-skyforecast-longform.mp4"));
            var shortformOutput = NormalizeOutputPath(loaded.Contract.Shortform.OutputPath, Path.Combine(renderDirectory, "shortform", "weekly-skyforecast-shortform.mp4"));
            var videoReportPath = Path.Combine(renderDirectory, "video-render-report.json");
            var ffmpegReportPath = Path.Combine(renderDirectory, "ffmpeg-execution-report.json");

            var longformResult = WeeklyExistingRunEpisodeRenderReportFactory.NotRequested(longformOutput);
            var shortformResult = WeeklyExistingRunEpisodeRenderReportFactory.NotRequested(shortformOutput);

            if (request.RenderLongform)
            {
                longformResult = await RenderEpisodeAsync("longform", loaded.Contract.Longform, loaded.Timeline.Longform, loaded.AudioPlan.LongformExpectedAudioPath, longformOutput, request, warnings, commandPlans, commandReports, cancellationToken);
            }

            if (request.RenderShortform)
            {
                shortformResult = await RenderEpisodeAsync("shortform", loaded.Contract.Shortform, loaded.Timeline.Shortform, loaded.AudioPlan.ShortformExpectedAudioPath, shortformOutput, request, warnings, commandPlans, commandReports, cancellationToken);
            }

            if (request.DryRun)
            {
                logger.LogInformation("WEEKLY_RENDER_DRY_RUN_COMMANDS_CREATED pipelineRunId={PipelineRunId} commandCount={CommandCount}", pipelineRunId, commandPlans.Count);
            }

            var completed = DateTime.UtcNow;
            var videoReport = new WeeklyExistingRunVideoRenderReport(pipelineRunId, started, completed, request.DryRun, longformResult, shortformResult, warnings, errors);
            var ffmpegReport = new WeeklyExistingRunFfmpegExecutionReport(pipelineRunId, started, completed, request.DryRun, commandReports, warnings, errors);
            await File.WriteAllTextAsync(videoReportPath, JsonSerializer.Serialize(videoReport, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(ffmpegReportPath, JsonSerializer.Serialize(ffmpegReport, JsonOptions), cancellationToken);

            logger.LogInformation("WEEKLY_RENDER_EXISTING_RUN_COMPLETE pipelineRunId={PipelineRunId} dryRun={DryRun}", pipelineRunId, request.DryRun);
            return new WeeklyExistingRunRenderResponse(
                pipelineRunId,
                errors.Count == 0 && (request.DryRun || longformResult.Rendered || longformResult.Skipped || shortformResult.Rendered || shortformResult.Skipped),
                request.DryRun,
                request.RenderLongform,
                longformResult.Rendered,
                longformResult.Skipped,
                longformOutput,
                request.RenderShortform,
                shortformResult.Rendered,
                shortformResult.Skipped,
                shortformOutput,
                videoReportPath,
                ffmpegReportPath,
                commandPlans,
                warnings,
                errors);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            logger.LogError(ex, "WEEKLY_RENDER_EXISTING_RUN_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
            throw;
        }
    }

    private async Task<WeeklyExistingRunEpisodeRenderReport> RenderEpisodeAsync(string episodeType, WeeklyEpisodeRenderContract contract, FinalRenderEpisodeTimeline timeline, string expectedAudioPath, string outputPath, WeeklyExistingRunRenderRequest request, List<string> warnings, List<WeeklyExistingRunFfmpegCommandPlan> commandPlans, List<WeeklyExistingRunFfmpegCommandReport> commandReports, CancellationToken cancellationToken)
    {
        var durationSeconds = contract.DurationSeconds > 0 ? contract.DurationSeconds : timeline.ActualDurationSeconds;
        logger.LogInformation(episodeType.Equals("longform", StringComparison.OrdinalIgnoreCase) ? "WEEKLY_RENDER_LONGFORM_START outputPath={OutputPath}" : "WEEKLY_RENDER_SHORTFORM_START outputPath={OutputPath}", outputPath);
        if (!request.DryRun && File.Exists(outputPath) && !request.OverwriteExisting)
        {
            var skippedInfo = new FileInfo(outputPath);
            commandReports.Add(new WeeklyExistingRunFfmpegCommandReport(episodeType, outputPath, true, false, true, null, 0, string.Empty, null, ["Output already exists and overwriteExisting is false."], []));
            logger.LogInformation(episodeType.Equals("longform", StringComparison.OrdinalIgnoreCase) ? "WEEKLY_RENDER_LONGFORM_COMPLETE outputPath={OutputPath} skipped={Skipped}" : "WEEKLY_RENDER_SHORTFORM_COMPLETE outputPath={OutputPath} skipped={Skipped}", outputPath, true);
            return new WeeklyExistingRunEpisodeRenderReport(true, false, true, outputPath, durationSeconds, skippedInfo.Length, false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var plan = await BuildCommandPlanAsync(episodeType, contract, timeline, expectedAudioPath, outputPath, warnings, cancellationToken);
        commandPlans.Add(plan);

        if (request.DryRun)
        {
            commandReports.Add(new WeeklyExistingRunFfmpegCommandReport(episodeType, outputPath, true, false, false, null, 0, plan.Command, null, [], []));
            logger.LogInformation(episodeType.Equals("longform", StringComparison.OrdinalIgnoreCase) ? "WEEKLY_RENDER_LONGFORM_COMPLETE outputPath={OutputPath} dryRun={DryRun}" : "WEEKLY_RENDER_SHORTFORM_COMPLETE outputPath={OutputPath} dryRun={DryRun}", outputPath, true);
            return new WeeklyExistingRunEpisodeRenderReport(true, false, false, outputPath, durationSeconds, 0, plan.AudioAttached);
        }

        var stopwatch = Stopwatch.StartNew();
        var processStart = new ProcessStartInfo
        {
            FileName = _renderingOptions.FfmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in BuildFfmpegArguments(plan.ConcatFilePath, plan.AudioPath, plan.AudioAttached, contract, outputPath))
        {
            processStart.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processStart) ?? throw new InvalidOperationException("Failed to start FFmpeg process.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _renderingOptions.FfmpegTimeoutSeconds)));
        await process.WaitForExitAsync(timeoutCts.Token);
        await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();

        var commandErrors = new List<string>();
        if (process.ExitCode != 0)
        {
            commandErrors.Add($"FFmpeg exited with code {process.ExitCode}.");
        }
        if (!File.Exists(outputPath))
        {
            commandErrors.Add($"Expected output was not created: {outputPath}");
        }

        commandReports.Add(new WeeklyExistingRunFfmpegCommandReport(episodeType, outputPath, true, true, false, process.ExitCode, stopwatch.ElapsedMilliseconds, plan.Command, Truncate(stderr, 12000), [], commandErrors));
        if (commandErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", commandErrors));
        }

        var output = new FileInfo(outputPath);
        logger.LogInformation(episodeType.Equals("longform", StringComparison.OrdinalIgnoreCase) ? "WEEKLY_RENDER_LONGFORM_COMPLETE outputPath={OutputPath} bytes={Bytes}" : "WEEKLY_RENDER_SHORTFORM_COMPLETE outputPath={OutputPath} bytes={Bytes}", outputPath, output.Length);
        return new WeeklyExistingRunEpisodeRenderReport(true, true, false, outputPath, durationSeconds, output.Length, plan.AudioAttached);
    }

    private async Task<WeeklyExistingRunFfmpegCommandPlan> BuildCommandPlanAsync(string episodeType, WeeklyEpisodeRenderContract contract, FinalRenderEpisodeTimeline timeline, string expectedAudioPath, string outputPath, List<string> warnings, CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)!)!, "temp", episodeType);
        Directory.CreateDirectory(tempDirectory);
        var segmentFiles = new List<string>();
        var index = 0;
        foreach (var shot in timeline.Segments.SelectMany(segment => segment.Shots))
        {
            index++;
            var segmentPath = Path.Combine(tempDirectory, $"{index:0000}-{SanitizeFileName(shot.AssetId)}.mp4");
            segmentFiles.Add(segmentPath);
            if (!File.Exists(segmentPath))
            {
                // Segment files are planned here. FFmpeg creates them via the concat demuxer command from still images at execution time.
            }
        }

        foreach (var transition in timeline.Segments.SelectMany(segment => segment.Shots).SelectMany(shot => new[] { shot.TransitionIn, shot.TransitionOut }).Where(t => !IsSupportedTransition(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"{episodeType} transition '{transition}' is not supported by the Phase 6.1 renderer and will fall back to a cut/fade-style concat.");
        }
        foreach (var motion in timeline.Segments.SelectMany(segment => segment.Shots).Select(shot => shot.MotionEffect).Where(m => !IsBasicMotionEffect(m)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"{episodeType} motion effect '{motion}' is not directly supported by the Phase 6.1 renderer and will fall back to basic scale/pad rendering.");
        }

        var concatPath = Path.Combine(tempDirectory, "concat-input.txt");
        var concatLines = timeline.Segments
            .SelectMany(segment => segment.Shots)
            .SelectMany(shot => new[] { $"file '{EscapeConcatPath(shot.AssetPath)}'", $"duration {Math.Max(1, shot.DurationSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)}" })
            .ToList();
        var lastShot = timeline.Segments.SelectMany(segment => segment.Shots).LastOrDefault();
        if (lastShot is not null)
        {
            concatLines.Add($"file '{EscapeConcatPath(lastShot.AssetPath)}'");
        }
        await File.WriteAllLinesAsync(concatPath, concatLines, cancellationToken);

        var audioPath = File.Exists(expectedAudioPath) ? expectedAudioPath : null;
        if (audioPath is null)
        {
            warnings.Add($"{episodeType} audio file was not found; rendering silent video. Expected: {expectedAudioPath}");
        }
        var command = BuildCommandString(_renderingOptions.FfmpegPath, concatPath, audioPath, audioPath is not null, contract, outputPath);
        return new WeeklyExistingRunFfmpegCommandPlan(episodeType, outputPath, concatPath, audioPath, audioPath is not null, command, segmentFiles);
    }

    private static IEnumerable<string> BuildFfmpegArguments(string concatPath, string? audioPath, bool audioAttached, WeeklyEpisodeRenderContract contract, string outputPath)
    {
        var width = contract.TargetWidth > 0 ? contract.TargetWidth : 1920;
        var height = contract.TargetHeight > 0 ? contract.TargetHeight : 1080;
        var fps = contract.Fps > 0 ? contract.Fps : 30;
        yield return "-y";
        yield return "-safe";
        yield return "0";
        yield return "-f";
        yield return "concat";
        yield return "-i";
        yield return concatPath;
        if (audioAttached && audioPath is not null)
        {
            yield return "-i";
            yield return audioPath;
        }
        yield return "-vf";
        yield return $"scale=w=iw*min({width}/iw\\,{height}/ih):h=ih*min({width}/iw\\,{height}/ih),pad={width}:{height}:({width}-iw)/2:({height}-ih)/2,fps={fps},format=yuv420p";
        yield return "-r";
        yield return fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return "-c:v";
        yield return "libx264";
        yield return "-preset";
        yield return "veryfast";
        yield return "-crf";
        yield return "22";
        if (audioAttached)
        {
            yield return "-c:a";
            yield return "aac";
            yield return "-b:a";
            yield return "128k";
            yield return "-shortest";
        }
        else
        {
            yield return "-an";
        }
        yield return "-movflags";
        yield return "+faststart";
        yield return outputPath;
    }

    private static string BuildCommandString(string ffmpegPath, string concatPath, string? audioPath, bool audioAttached, WeeklyEpisodeRenderContract contract, string outputPath)
    {
        var width = contract.TargetWidth > 0 ? contract.TargetWidth : 1920;
        var height = contract.TargetHeight > 0 ? contract.TargetHeight : 1080;
        var fps = contract.Fps > 0 ? contract.Fps : 30;
        var input = $"-safe 0 -f concat -i {Quote(concatPath)}";
        var audio = audioAttached && audioPath is not null ? $" -i {Quote(audioPath)}" : string.Empty;
        var audioEncoding = audioAttached ? " -c:a aac -b:a 128k -shortest" : " -an";
        return $"{Quote(ffmpegPath)} -y {input}{audio} -vf {Quote($"scale=w=iw*min({width}/iw\\,{height}/ih):h=ih*min({width}/iw\\,{height}/ih),pad={width}:{height}:({width}-iw)/2:({height}-ih)/2,fps={fps},format=yuv420p")} -r {fps} -c:v libx264 -preset veryfast -crf 22{audioEncoding} -movflags +faststart {Quote(outputPath)}";
    }

    private string ResolveWorkingDirectoryRoot(Guid pipelineRunId)
    {
        var workingRoot = string.IsNullOrWhiteSpace(_renderingOptions.WorkingDirectory) ? "./media-output" : _renderingOptions.WorkingDirectory;
        if (!Directory.Exists(workingRoot))
        {
            throw new DirectoryNotFoundException($"Pipeline working directory root does not exist: {workingRoot}");
        }

        var matches = Directory.EnumerateDirectories(workingRoot, pipelineRunId.ToString("N"), SearchOption.AllDirectories)
            .Concat(Directory.EnumerateDirectories(workingRoot, pipelineRunId.ToString("D"), SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsWeeklyRenderRunDirectory)
            .ToList();
        return matches.Count switch
        {
            0 => throw new DirectoryNotFoundException($"No WeeklySkyForecast workingDirectoryRoot was found for pipelineRunId {pipelineRunId} under {workingRoot}."),
            1 => matches[0],
            _ => matches.OrderByDescending(Directory.GetLastWriteTimeUtc).First()
        };
    }

    private static bool IsWeeklyRenderRunDirectory(string path)
        => File.Exists(Path.Combine(path, "render", "weekly-render-contract.json")) || File.Exists(Path.Combine(path, "episode", "final-render-timeline.json"));

    private static async Task<WeeklyExistingRunLoadedInputs> LoadInputsAsync(WeeklyExistingRunRequiredPaths paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths.All)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required render input file is missing: {path}", path);
            }
        }

        return new WeeklyExistingRunLoadedInputs(
            await ReadJsonAsync<WeeklyRenderContract>(paths.RenderContract, cancellationToken),
            await ReadJsonAsync<WeeklyRenderInputManifest>(paths.InputManifest, cancellationToken),
            await ReadJsonAsync<WeeklyFfmpegFilterGraphPlan>(paths.FilterGraphPlan, cancellationToken),
            await ReadJsonAsync<WeeklyTransitionExecutionPlan>(paths.TransitionPlan, cancellationToken),
            await ReadJsonAsync<WeeklyMotionEffectPlan>(paths.MotionPlan, cancellationToken),
            await ReadJsonAsync<WeeklyAudioAlignmentPlan>(paths.AudioPlan, cancellationToken),
            await ReadJsonAsync<FinalRenderTimeline>(paths.FinalTimeline, cancellationToken),
            await ReadJsonAsync<IReadOnlyList<FinalRenderShotListEntry>>(paths.FinalShotList, cancellationToken));
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
        => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions) ?? throw new InvalidOperationException($"Unable to deserialize required render input file: {path}");

    private static void ValidateInputs(Guid pipelineRunId, string root, WeeklyExistingRunRenderRequest request, WeeklyExistingRunLoadedInputs loaded, List<string> errors)
    {
        if (pipelineRunId == Guid.Empty) errors.Add("pipelineRunId is required.");
        if (!Directory.Exists(root)) errors.Add($"workingDirectoryRoot does not exist: {root}");
        if (loaded.Contract.PipelineRunId != pipelineRunId) errors.Add($"Render contract pipelineRunId {loaded.Contract.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (loaded.Timeline.PipelineRunId != pipelineRunId) errors.Add($"Final render timeline pipelineRunId {loaded.Timeline.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (loaded.Manifest.PipelineRunId != pipelineRunId) errors.Add($"Input manifest pipelineRunId {loaded.Manifest.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (request.RenderLongform && !loaded.Contract.Longform.Enabled) errors.Add("Longform contract is not enabled.");
        if (request.RenderShortform && !loaded.Contract.Shortform.Enabled) errors.Add("Shortform contract is not enabled.");
        if (request.RenderLongform && loaded.Timeline.Longform.Segments.Count == 0) errors.Add("Final render timeline has no longform segments.");
        if (request.RenderShortform && loaded.Timeline.Shortform.Segments.Count == 0) errors.Add("Final render timeline has no shortform segments.");

        foreach (var asset in loaded.Manifest.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.AssetPath))
            {
                errors.Add($"Asset {asset.AssetId} has an empty asset path.");
                continue;
            }
            if (!File.Exists(asset.AssetPath))
            {
                errors.Add($"Asset file is missing: {asset.AssetPath}");
                continue;
            }
            try
            {
                using var stream = File.Open(asset.AssetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (!stream.CanRead) errors.Add($"Asset file is not readable: {asset.AssetPath}");
            }
            catch (Exception ex)
            {
                errors.Add($"Asset file is not readable: {asset.AssetPath}. {ex.Message}");
            }
        }
    }

    private static string NormalizeOutputPath(string? contractPath, string fallback)
        => string.IsNullOrWhiteSpace(contractPath) ? fallback : contractPath;

    private static bool IsSupportedTransition(string? transition)
        => string.IsNullOrWhiteSpace(transition)
            || transition.Equals("cut", StringComparison.OrdinalIgnoreCase)
            || transition.Equals("fade", StringComparison.OrdinalIgnoreCase)
            || transition.Equals("fadein", StringComparison.OrdinalIgnoreCase)
            || transition.Equals("fadeout", StringComparison.OrdinalIgnoreCase)
            || transition.Equals("crossfade", StringComparison.OrdinalIgnoreCase);

    private static bool IsBasicMotionEffect(string? motion)
        => string.IsNullOrWhiteSpace(motion)
            || motion.Equals("none", StringComparison.OrdinalIgnoreCase)
            || motion.Equals("slow-drift", StringComparison.OrdinalIgnoreCase)
            || motion.Equals("gentle-zoom-in", StringComparison.OrdinalIgnoreCase)
            || motion.Equals("subtle-ken-burns", StringComparison.OrdinalIgnoreCase);

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static string EscapeConcatPath(string path) => path.Replace("'", "'\\''", StringComparison.Ordinal);
    private static string SanitizeFileName(string value) => string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim('_');
    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record WeeklyExistingRunLoadedInputs(
    WeeklyRenderContract Contract,
    WeeklyRenderInputManifest Manifest,
    WeeklyFfmpegFilterGraphPlan FilterGraphPlan,
    WeeklyTransitionExecutionPlan TransitionPlan,
    WeeklyMotionEffectPlan MotionPlan,
    WeeklyAudioAlignmentPlan AudioPlan,
    FinalRenderTimeline Timeline,
    IReadOnlyList<FinalRenderShotListEntry> ShotList);

internal sealed record WeeklyExistingRunRequiredPaths(
    string RenderContract,
    string InputManifest,
    string FilterGraphPlan,
    string TransitionPlan,
    string MotionPlan,
    string AudioPlan,
    string FinalTimeline,
    string FinalShotList)
{
    public IReadOnlyList<string> All => [RenderContract, InputManifest, FilterGraphPlan, TransitionPlan, MotionPlan, AudioPlan, FinalTimeline, FinalShotList];

    public static WeeklyExistingRunRequiredPaths FromRoot(string root)
        => new(
            Path.Combine(root, "render", "weekly-render-contract.json"),
            Path.Combine(root, "render", "render-input-manifest.json"),
            Path.Combine(root, "render", "ffmpeg-filtergraph-plan.json"),
            Path.Combine(root, "render", "transition-execution-plan.json"),
            Path.Combine(root, "render", "motion-effect-execution-plan.json"),
            Path.Combine(root, "render", "audio-alignment-plan.json"),
            Path.Combine(root, "episode", "final-render-timeline.json"),
            Path.Combine(root, "episode", "final-render-shot-list.json"));
}

internal static class WeeklyExistingRunEpisodeRenderReportFactory
{
    public static WeeklyExistingRunEpisodeRenderReport NotRequested(string outputPath) => new(false, false, false, outputPath, 0, 0, false);
}
