using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
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
    string RenderQualityReportPath,
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
    IReadOnlyList<string> SegmentFiles,
    IReadOnlyList<string> Arguments,
    WeeklyExistingRunEpisodeQualityMetrics QualityMetrics);

public sealed record WeeklyExistingRunEpisodeQualityMetrics(
    string EpisodeType,
    int MaxShotDurationSeconds,
    int RepeatedAssetPathCount,
    bool MoonOnlyStellariumDetected,
    int PlanetGroupingFramesUsed,
    int MotionEffectsAppliedCount,
    int TransitionEffectsAppliedCount,
    int FallbackTransitionCount,
    int FallbackMotionCount,
    bool PacingPassed,
    bool VisualDistributionPassed);

public sealed record WeeklyRenderQualityReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    int MaxLongformShotDurationSeconds,
    int MaxShortformShotDurationSeconds,
    int RepeatedAssetPathCount,
    bool MoonOnlyStellariumDetected,
    int PlanetGroupingFramesUsed,
    int MotionEffectsAppliedCount,
    int TransitionEffectsAppliedCount,
    int FallbackTransitionCount,
    int FallbackMotionCount,
    bool ShortformPacingPassed,
    bool LongformPacingPassed,
    bool VisualDistributionPassed,
    IReadOnlyList<WeeklyExistingRunEpisodeQualityMetrics> EpisodeMetrics,
    IReadOnlyList<string> Warnings);

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
            var qualityReportPath = Path.Combine(renderDirectory, "render-quality-report.json");

            var longformResult = WeeklyExistingRunEpisodeRenderReportFactory.NotRequested(longformOutput);
            var shortformResult = WeeklyExistingRunEpisodeRenderReportFactory.NotRequested(shortformOutput);

            if (request.RenderLongform)
            {
                longformResult = await RenderEpisodeAsync("longform", loaded.Contract.Longform, loaded.Timeline.Longform, loaded.Manifest, loaded.ProductionAssetManifest, loaded.AudioPlan.LongformExpectedAudioPath, longformOutput, request, warnings, commandPlans, commandReports, cancellationToken);
            }

            if (request.RenderShortform)
            {
                shortformResult = await RenderEpisodeAsync("shortform", loaded.Contract.Shortform, loaded.Timeline.Shortform, loaded.Manifest, loaded.ProductionAssetManifest, loaded.AudioPlan.ShortformExpectedAudioPath, shortformOutput, request, warnings, commandPlans, commandReports, cancellationToken);
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
            var qualityReport = BuildQualityReport(pipelineRunId, commandPlans, warnings);
            await File.WriteAllTextAsync(qualityReportPath, JsonSerializer.Serialize(qualityReport, JsonOptions), cancellationToken);

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
                qualityReportPath,
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

    private async Task<WeeklyExistingRunEpisodeRenderReport> RenderEpisodeAsync(string episodeType, WeeklyEpisodeRenderContract contract, FinalRenderEpisodeTimeline timeline, WeeklyRenderInputManifest manifest, WeeklyProductionAssetManifest? productionManifest, string expectedAudioPath, string outputPath, WeeklyExistingRunRenderRequest request, List<string> warnings, List<WeeklyExistingRunFfmpegCommandPlan> commandPlans, List<WeeklyExistingRunFfmpegCommandReport> commandReports, CancellationToken cancellationToken)
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

        var plan = await BuildCommandPlanAsync(episodeType, contract, timeline, manifest, productionManifest, expectedAudioPath, outputPath, warnings, cancellationToken);
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
        foreach (var argument in plan.Arguments)
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

    private async Task<WeeklyExistingRunFfmpegCommandPlan> BuildCommandPlanAsync(string episodeType, WeeklyEpisodeRenderContract contract, FinalRenderEpisodeTimeline timeline, WeeklyRenderInputManifest manifest, WeeklyProductionAssetManifest? productionManifest, string expectedAudioPath, string outputPath, List<string> warnings, CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)!)!, "temp", episodeType);
        Directory.CreateDirectory(tempDirectory);

        var refinedTimeline = RefineTimelineForRender(episodeType, timeline, manifest, productionManifest, warnings);
        var shots = refinedTimeline.Segments.SelectMany(segment => segment.Shots.Select(shot => (Segment: segment, Shot: shot))).ToList();
        var segmentFiles = new List<string>();
        var index = 0;
        foreach (var (_, shot) in shots)
        {
            index++;
            segmentFiles.Add(Path.Combine(tempDirectory, $"{index:0000}-{SanitizeFileName(shot.AssetId)}.mp4"));
        }

        var concatPath = Path.Combine(tempDirectory, "shot-plan.json");
        await File.WriteAllTextAsync(concatPath, JsonSerializer.Serialize(refinedTimeline, JsonOptions), cancellationToken);

        var audioPath = File.Exists(expectedAudioPath) ? expectedAudioPath : null;
        if (audioPath is null)
        {
            warnings.Add($"{episodeType} audio file was not found; rendering silent video. Expected: {expectedAudioPath}");
        }

        var qualityMetrics = BuildEpisodeQualityMetrics(episodeType, refinedTimeline);
        if (!qualityMetrics.PacingPassed)
        {
            warnings.Add($"{episodeType} pacing limits failed after render refinement; max shot duration is {qualityMetrics.MaxShotDurationSeconds}s.");
        }
        if (qualityMetrics.MoonOnlyStellariumDetected)
        {
            warnings.Add($"{episodeType} visual distribution still appears moon-only for Stellarium shots.");
        }

        var arguments = BuildFfmpegArguments(refinedTimeline, audioPath, audioPath is not null, contract, outputPath).ToList();
        var command = BuildCommandString(_renderingOptions.FfmpegPath, arguments);
        return new WeeklyExistingRunFfmpegCommandPlan(episodeType, outputPath, concatPath, audioPath, audioPath is not null, command, segmentFiles, arguments, qualityMetrics);
    }

    private static FinalRenderEpisodeTimeline RefineTimelineForRender(string episodeType, FinalRenderEpisodeTimeline timeline, WeeklyRenderInputManifest manifest, WeeklyProductionAssetManifest? productionManifest, List<string> warnings)
    {
        var shortform = episodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase);
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var segments = new List<FinalRenderSegment>();
        foreach (var segment in timeline.Segments ?? [])
        {
            var segmentShots = segment.Shots ?? [];
            var pool = SelectRenderAssets(segment, shortform, manifest, productionManifest).ToList();
            if (pool.Count == 0)
            {
                pool = segmentShots.Select(s => new RenderAssetCandidate(s.AssetId, s.AssetType, s.AssetPath)).Where(a => !string.IsNullOrWhiteSpace(a.AssetPath)).DistinctBy(a => a.AssetPath, StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (pool.Count == 0)
            {
                warnings.Add($"{episodeType} segment {segment.SegmentId} has no usable render assets after refinement.");
                segments.Add(segment);
                continue;
            }

            var maxShotDuration = GetMaxShotDurationSeconds(segment.SegmentType, shortform);
            var shotCount = Math.Max(1, (int)Math.Ceiling(segment.DurationSeconds / (double)maxShotDuration));
            var preferred = shortform ? Math.Max(1, Math.Min(pool.Count, segment.DurationSeconds / Math.Max(1, maxShotDuration))) : Math.Min(pool.Count, Math.Max(1, segment.DurationSeconds / 7));
            shotCount = Math.Max(shotCount, preferred);

            var baseDuration = segment.DurationSeconds / shotCount;
            var remainder = segment.DurationSeconds % shotCount;
            var cursor = segment.StartSecond;
            var shots = new List<FinalRenderShot>();
            for (var i = 0; i < shotCount; i++)
            {
                var duration = baseDuration + (i < remainder ? 1 : 0);
                var asset = PickAsset(pool, usage, shortform ? 1 : 2, i);
                usage[asset.AssetPath] = usage.TryGetValue(asset.AssetPath, out var count) ? count + 1 : 1;
                var start = cursor;
                var end = i == shotCount - 1 ? segment.EndSecond : cursor + duration;
                var transitionIn = i == 0 ? (segment.StartSecond == 0 ? "FadeIn" : "CrossFade") : ResolveRenderTransition(shots[^1].AssetType, asset.AssetType, segment.SegmentType, shortform);
                var next = pool[(i + 1) % pool.Count];
                var transitionOut = i == shotCount - 1 ? "FadeOut" : ResolveRenderTransition(asset.AssetType, next.AssetType, segment.SegmentType, shortform);
                shots.Add(new FinalRenderShot(i + 1, asset.AssetId, asset.AssetType, asset.AssetPath, start, end, Math.Max(1, end - start), transitionIn, transitionOut, ResolveRenderMotion(asset.AssetType, segment.SegmentType), i == 0 ? $"render-refined primary visual for {segment.SegmentType}" : "render-refined supporting visual variety"));
                cursor = end;
            }
            segments.Add(segment with { Shots = shots, StartSecond = segment.StartSecond, EndSecond = segment.EndSecond, DurationSeconds = segment.DurationSeconds });
        }
        return timeline with { Segments = segments, ActualDurationSeconds = segments.Sum(s => s.DurationSeconds) };
    }

    private static IEnumerable<RenderAssetCandidate> SelectRenderAssets(FinalRenderSegment segment, bool shortform, WeeklyRenderInputManifest manifest, WeeklyProductionAssetManifest? productionManifest)
    {
        var all = new List<RenderAssetCandidate>();
        if (productionManifest is not null)
        {
            all.AddRange((productionManifest.SegmentBundles ?? [])
                .Where(bundle => bundle is not null &&
                    (string.Equals(bundle.SegmentId, segment.SegmentId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(bundle.SegmentType, segment.SegmentType, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(bundle => bundle.AssignedVisualAssets ?? [])
                .Where(asset => asset is not null && asset.Exists && !string.IsNullOrWhiteSpace(asset.FilePath))
                .Select(asset => new RenderAssetCandidate(asset.AssetId, NormalizeRenderAssetType(asset.SourceType.ToString()), asset.FilePath)));
        }
        all.AddRange((manifest?.Assets ?? []).Where(asset => asset is not null && asset.Exists && !string.IsNullOrWhiteSpace(asset.AssetPath)).Select(asset => new RenderAssetCandidate(asset.AssetId, NormalizeRenderAssetType(asset.AssetType), asset.AssetPath)));
        all.AddRange((segment.Shots ?? []).Where(shot => shot is not null && !string.IsNullOrWhiteSpace(shot.AssetPath)).Select(shot => new RenderAssetCandidate(shot.AssetId, NormalizeRenderAssetType(shot.AssetType), shot.AssetPath)));

        var preferred = all.Where(asset => SegmentAssetScore(segment.SegmentType, asset) > 0)
            .OrderByDescending(asset => SegmentAssetScore(segment.SegmentType, asset))
            .ThenBy(asset => asset.AssetPath, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(asset => asset.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (preferred.Count > 0) return preferred;
        return all.OrderByDescending(asset => GenericAssetScore(segment.SegmentType, asset)).DistinctBy(asset => asset.AssetPath, StringComparer.OrdinalIgnoreCase);
    }

    private static int SegmentAssetScore(string segmentType, RenderAssetCandidate asset)
    {
        var path = asset.AssetPath.Replace('\\', '/');
        var id = asset.AssetId;
        var haystack = $"{id} {path} {asset.AssetType}";
        return segmentType switch
        {
            "OpeningHook" => ContainsAny(haystack, "AICinematic", "ai-cinematic", "cinematic") ? 90 : ContainsAny(haystack, "wide", "Stellarium") ? 70 : 0,
            "WeeklySkyOverview" => ContainsAny(haystack, "MotionGraphic", "motion", "overview") ? 100 : ContainsAny(haystack, "wide", "Stellarium") ? 80 : ContainsAny(haystack, "reset", "AICinematic") ? 60 : 0,
            "HeroEvent" or "StrongestEvent" => ContainsAny(haystack, "western_planet_grouping_scene", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide") ? 120 : ContainsAny(haystack, "planet", "alignment", "Venus", "Saturn") ? 90 : 0,
            "MoonHighlights" => ContainsAny(haystack, "moon_hero_scene", "moon") ? 120 : 0,
            "PlanetHighlights" => ContainsAny(haystack, "western_planet_grouping_scene", "planet", "Venus", "Saturn", "alignment") ? 120 : 0,
            "BestObservationWindow" => ContainsAny(haystack, "best-time", "where-to-look", "WhereToLook", "MotionGraphic", "motion") ? 120 : 0,
            "AstrophotographyTip" => ContainsAny(haystack, "ExpandedStellarium", "expanded", "night") ? 120 : ContainsAny(haystack, "AICinematic", "cinematic") ? 90 : 0,
            "WeeklySummary" => ContainsAny(haystack, "AICinematic", "cinematic") ? 100 : ContainsAny(haystack, "weekly-summary-card", "summary-card") ? 90 : 0,
            "CallToAction" => ContainsAny(haystack, "MotionGraphic", "AICinematic", "call-to-action", "cta") ? 100 : 0,
            _ => 0
        };
    }

    private static int GenericAssetScore(string segmentType, RenderAssetCandidate asset)
        => asset.AssetType switch
        {
            "AICinematic" => 70,
            "MotionGraphic" => 65,
            "Stellarium" => segmentType.Contains("Moon", StringComparison.OrdinalIgnoreCase) ? 80 : 60,
            "ExpandedStellarium" => 55,
            _ => 30
        };

    private static RenderAssetCandidate PickAsset(IReadOnlyList<RenderAssetCandidate> pool, Dictionary<string, int> usage, int preferredLimit, int index)
    {
        for (var offset = 0; offset < pool.Count; offset++)
        {
            var candidate = pool[(index + offset) % pool.Count];
            if (!usage.TryGetValue(candidate.AssetPath, out var count) || count < preferredLimit) return candidate;
        }
        return pool[index % pool.Count];
    }

    private static int GetMaxShotDurationSeconds(string segmentType, bool shortform)
        => shortform
            ? segmentType switch { "StrongestEvent" => 8, "CallToAction" => 4, _ => 5 }
            : segmentType.Equals("HeroEvent", StringComparison.OrdinalIgnoreCase) ? 14 : 12;

    private static string ResolveRenderTransition(string? fromType, string toType, string segmentType, bool shortform)
    {
        if (string.IsNullOrWhiteSpace(fromType)) return "FadeIn";
        if (segmentType is "HeroEvent" or "StrongestEvent") return "SlowDissolve";
        if (shortform) return "CrossFade";
        if (fromType.Equals(toType, StringComparison.OrdinalIgnoreCase)) return "Dissolve";
        return toType.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase) ? "CrossFade" : "Dissolve";
    }

    private static string ResolveRenderMotion(string assetType, string segmentType)
        => segmentType switch
        {
            "HeroEvent" or "StrongestEvent" => assetType.Equals("Stellarium", StringComparison.OrdinalIgnoreCase) ? "SubtlePan" : "SlowPushIn",
            "WeeklySummary" => "SlowZoomOut",
            _ => assetType switch
            {
                "AICinematic" => "SlowZoomIn",
                "Stellarium" => "SubtlePan",
                "ExpandedStellarium" => "SlowPushIn",
                "MotionGraphic" => "StaticHold",
                _ => "SlowZoomIn"
            }
        };

    private static IEnumerable<string> BuildFfmpegArguments(FinalRenderEpisodeTimeline timeline, string? audioPath, bool audioAttached, WeeklyEpisodeRenderContract contract, string outputPath)
    {
        var width = contract.TargetWidth > 0 ? contract.TargetWidth : 1920;
        var height = contract.TargetHeight > 0 ? contract.TargetHeight : 1080;
        var fps = contract.Fps > 0 ? contract.Fps : 30;
        var shots = timeline.Segments.SelectMany(segment => segment.Shots).ToList();
        yield return "-y";
        foreach (var shot in shots)
        {
            yield return "-loop";
            yield return "1";
            yield return "-t";
            yield return "0.1";
            yield return "-i";
            yield return shot.AssetPath;
        }
        if (audioAttached && audioPath is not null)
        {
            yield return "-i";
            yield return audioPath;
        }
        yield return "-filter_complex";
        yield return BuildFilterComplex(shots, width, height, fps);
        yield return "-map";
        yield return shots.Count == 0 ? "0:v" : "[vout]";
        if (audioAttached)
        {
            yield return "-map";
            yield return $"{shots.Count}:a:0";
        }
        yield return "-r";
        yield return fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return "-c:v";
        yield return "libx264";
        yield return "-preset";
        yield return "veryfast";
        yield return "-crf";
        yield return "20";
        if (audioAttached)
        {
            yield return "-c:a";
            yield return "aac";
            yield return "-b:a";
            yield return "160k";
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

    private static string BuildFilterComplex(IReadOnlyList<FinalRenderShot> shots, int width, int height, int fps)
    {
        if (shots.Count == 0) return string.Empty;
        var parts = new List<string>();
        for (var i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            var frames = Math.Max(1, shot.DurationSeconds * fps);
            var zoom = BuildZoomExpression(shot.MotionEffect);
            var pan = BuildPanExpression(shot.MotionEffect);
            var fade = BuildShotFadeFilters(shot, fps);
            parts.Add($"[{i}:v]scale={width * 2}:{height * 2}:force_original_aspect_ratio=increase,crop={width * 2}:{height * 2},zoompan=z='{zoom}':x='{pan.X}':y='{pan.Y}':d={frames}:s={width}x{height}:fps={fps},trim=duration={shot.DurationSeconds},setpts=PTS-STARTPTS{fade},format=yuv420p[v{i}]");
        }
        if (shots.Count == 1)
        {
            parts.Add("[v0]null[vout]");
            return string.Join(';', parts);
        }
        var cumulative = (double)shots[0].DurationSeconds;
        var previous = "v0";
        for (var i = 1; i < shots.Count; i++)
        {
            var transitionSeconds = GetTransitionDurationSeconds(shots[i - 1].TransitionOut, shots[i].TransitionIn, shots[i - 1].DurationSeconds, shots[i].DurationSeconds);
            var offset = Math.Max(0.05, cumulative - transitionSeconds);
            var label = i == shots.Count - 1 ? "vout" : $"xf{i}";
            parts.Add($"[{previous}][v{i}]xfade=transition=fade:duration={transitionSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}:offset={offset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}[{label}]");
            cumulative += shots[i].DurationSeconds - transitionSeconds;
            previous = label;
        }
        return string.Join(';', parts);
    }

    private static string BuildZoomExpression(string motion) => motion switch
    {
        "SlowZoomIn" or "SlowPushIn" => "min(zoom+0.0012,1.16)",
        "SlowZoomOut" => "if(eq(on,0),1.16,max(1.0,zoom-0.0010))",
        "SubtlePan" => "1.08",
        _ => "1.0"
    };

    private static (string X, string Y) BuildPanExpression(string motion) => motion switch
    {
        "SubtlePan" => ("(iw-iw/zoom)*min(on/300,1)", "(ih-ih/zoom)*0.35"),
        "SlowZoomOut" => ("(iw-iw/zoom)/2", "(ih-ih/zoom)/2"),
        _ => ("(iw-iw/zoom)/2", "(ih-ih/zoom)/2")
    };

    private static string BuildShotFadeFilters(FinalRenderShot shot, int fps)
    {
        var filters = new List<string>();
        if (shot.TransitionIn.Equals("FadeIn", StringComparison.OrdinalIgnoreCase)) filters.Add($"fade=t=in:st=0:d={Math.Min(1, Math.Max(0.25, shot.DurationSeconds / 4.0)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        if (shot.TransitionOut.Equals("FadeOut", StringComparison.OrdinalIgnoreCase)) filters.Add($"fade=t=out:st={Math.Max(0, shot.DurationSeconds - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}:d={Math.Min(1, Math.Max(0.25, shot.DurationSeconds / 4.0)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        return filters.Count == 0 ? string.Empty : "," + string.Join(',', filters);
    }

    private static double GetTransitionDurationSeconds(string transitionOut, string transitionIn, int previousDuration, int currentDuration)
    {
        var name = transitionOut.Equals("FadeOut", StringComparison.OrdinalIgnoreCase) ? transitionIn : transitionOut;
        var requested = name.Equals("SlowDissolve", StringComparison.OrdinalIgnoreCase) ? 1.5 : 0.6;
        return Math.Min(requested, Math.Max(0.1, Math.Min(previousDuration, currentDuration) / 3.0));
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


    private static string BuildCommandString(string ffmpegPath, IReadOnlyList<string> arguments)
        => $"{Quote(ffmpegPath)} {string.Join(" ", arguments.Select(QuoteArgument))}";

    private static string QuoteArgument(string value)
        => string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal) || value.Contains(';', StringComparison.Ordinal) || value.Contains('[', StringComparison.Ordinal) || value.Contains(']', StringComparison.Ordinal)
            ? Quote(value)
            : value;

    private static WeeklyRenderQualityReport BuildQualityReport(Guid pipelineRunId, IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans, IReadOnlyList<string> warnings)
    {
        var longform = plans.FirstOrDefault(p => p.EpisodeType.Equals("longform", StringComparison.OrdinalIgnoreCase))?.QualityMetrics;
        var shortform = plans.FirstOrDefault(p => p.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase))?.QualityMetrics;
        var metrics = plans.Select(p => p.QualityMetrics).ToList();
        return new WeeklyRenderQualityReport(
            pipelineRunId,
            DateTime.UtcNow,
            longform?.MaxShotDurationSeconds ?? 0,
            shortform?.MaxShotDurationSeconds ?? 0,
            metrics.Sum(m => m.RepeatedAssetPathCount),
            metrics.Any(m => m.MoonOnlyStellariumDetected),
            metrics.Sum(m => m.PlanetGroupingFramesUsed),
            metrics.Sum(m => m.MotionEffectsAppliedCount),
            metrics.Sum(m => m.TransitionEffectsAppliedCount),
            metrics.Sum(m => m.FallbackTransitionCount),
            metrics.Sum(m => m.FallbackMotionCount),
            shortform?.PacingPassed ?? true,
            longform?.PacingPassed ?? true,
            metrics.All(m => m.VisualDistributionPassed) && metrics.Sum(m => m.PlanetGroupingFramesUsed) >= 3 && !metrics.Any(m => m.MoonOnlyStellariumDetected),
            metrics,
            warnings);
    }

    private static WeeklyExistingRunEpisodeQualityMetrics BuildEpisodeQualityMetrics(string episodeType, FinalRenderEpisodeTimeline timeline)
    {
        var shortform = episodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase);
        var shots = timeline.Segments.SelectMany(s => s.Shots.Select(shot => (Segment: s, Shot: shot))).ToList();
        var maxShot = shots.Count == 0 ? 0 : shots.Max(x => x.Shot.DurationSeconds);
        var allowedRepeat = shortform ? 1 : 2;
        var repeated = shots.GroupBy(x => x.Shot.AssetPath, StringComparer.OrdinalIgnoreCase).Sum(g => Math.Max(0, g.Count() - allowedRepeat));
        var stellarium = shots.Where(x => x.Shot.AssetType.Equals("Stellarium", StringComparison.OrdinalIgnoreCase) || x.Shot.AssetPath.Contains("stellarium", StringComparison.OrdinalIgnoreCase)).ToList();
        var moonOnly = stellarium.Count > 0 && stellarium.All(x => x.Shot.AssetPath.Contains("moon", StringComparison.OrdinalIgnoreCase) || x.Shot.AssetId.Contains("moon", StringComparison.OrdinalIgnoreCase));
        var grouping = shots.Count(x => ContainsAny(x.Shot.AssetPath + " " + x.Shot.AssetId, "western_planet_grouping_scene", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide"));
        var motionApplied = shots.Count(x => IsSupportedMotion(x.Shot.MotionEffect));
        var fallbackMotion = shots.Count(x => !IsSupportedMotion(x.Shot.MotionEffect));
        var transitions = shots.Sum(x => (IsXfadeTransition(x.Shot.TransitionIn) ? 1 : 0) + (IsXfadeTransition(x.Shot.TransitionOut) ? 1 : 0));
        var fallbackTransitions = shots.Sum(x => (IsSupportedRenderTransition(x.Shot.TransitionIn) ? 0 : 1) + (IsSupportedRenderTransition(x.Shot.TransitionOut) ? 0 : 1));
        var pacing = shots.All(x => x.Shot.DurationSeconds <= GetMaxShotDurationSeconds(x.Segment.SegmentType, shortform));
        var groupingThreshold = shortform ? 1 : Math.Min(3, shots.Count);
        var distribution = !moonOnly && (!timeline.Segments.Any(s => s.SegmentType is "HeroEvent" or "StrongestEvent" or "PlanetHighlights") || grouping >= groupingThreshold);
        return new WeeklyExistingRunEpisodeQualityMetrics(episodeType, maxShot, repeated, moonOnly, grouping, motionApplied, transitions, fallbackTransitions, fallbackMotion, pacing, distribution);
    }

    private static bool ContainsAny(string value, params string[] needles) => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    private static bool IsSupportedMotion(string? motion) => motion is "StaticHold" or "SlowZoomIn" or "SlowZoomOut" or "SlowPushIn" or "SubtlePan";
    private static bool IsXfadeTransition(string? transition) => transition is "CrossFade" or "Dissolve" or "SlowDissolve" or "CinematicFade" or "Fade";
    private static bool IsSupportedRenderTransition(string? transition) => string.IsNullOrWhiteSpace(transition) || transition is "Cut" or "SoftCut" or "Fade" or "FadeIn" or "FadeOut" or "CrossFade" or "Dissolve" or "SlowDissolve" or "CinematicFade";

    private static string NormalizeRenderAssetType(string value) => value switch
    {
        "StellariumBase" => "Stellarium",
        "StellariumExpanded" => "ExpandedStellarium",
        "MotionGraphics" => "MotionGraphic",
        "EducationalOverlay" => "MotionGraphic",
        _ => value
    };

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

        var productionManifest = File.Exists(paths.ProductionAssetManifest)
            ? await ReadJsonAsync<WeeklyProductionAssetManifest>(paths.ProductionAssetManifest, cancellationToken)
            : null;

        return new WeeklyExistingRunLoadedInputs(
            await ReadJsonAsync<WeeklyRenderContract>(paths.RenderContract, cancellationToken),
            await ReadJsonAsync<WeeklyRenderInputManifest>(paths.InputManifest, cancellationToken),
            await ReadJsonAsync<WeeklyFfmpegFilterGraphPlan>(paths.FilterGraphPlan, cancellationToken),
            await ReadJsonAsync<WeeklyTransitionExecutionPlan>(paths.TransitionPlan, cancellationToken),
            await ReadJsonAsync<WeeklyMotionEffectPlan>(paths.MotionPlan, cancellationToken),
            await ReadJsonAsync<WeeklyAudioAlignmentPlan>(paths.AudioPlan, cancellationToken),
            await ReadJsonAsync<FinalRenderTimeline>(paths.FinalTimeline, cancellationToken),
            await ReadJsonAsync<IReadOnlyList<FinalRenderShotListEntry>>(paths.FinalShotList, cancellationToken),
            productionManifest);
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

internal sealed record RenderAssetCandidate(string AssetId, string AssetType, string AssetPath);

internal sealed record WeeklyExistingRunLoadedInputs(
    WeeklyRenderContract Contract,
    WeeklyRenderInputManifest Manifest,
    WeeklyFfmpegFilterGraphPlan FilterGraphPlan,
    WeeklyTransitionExecutionPlan TransitionPlan,
    WeeklyMotionEffectPlan MotionPlan,
    WeeklyAudioAlignmentPlan AudioPlan,
    FinalRenderTimeline Timeline,
    IReadOnlyList<FinalRenderShotListEntry> ShotList,
    WeeklyProductionAssetManifest? ProductionAssetManifest);

internal sealed record WeeklyExistingRunRequiredPaths(
    string RenderContract,
    string InputManifest,
    string FilterGraphPlan,
    string TransitionPlan,
    string MotionPlan,
    string AudioPlan,
    string FinalTimeline,
    string FinalShotList,
    string ProductionAssetManifest)
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
            Path.Combine(root, "episode", "final-render-shot-list.json"),
            Path.Combine(root, "episode", "weekly-production-asset-manifest.json"));
}

internal static class WeeklyExistingRunEpisodeRenderReportFactory
{
    public static WeeklyExistingRunEpisodeRenderReport NotRequested(string outputPath) => new(false, false, false, outputPath, 0, 0, false);
}
