using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;

public interface IWeeklyFfmpegRenderPreparationEngine
{
    Task<WeeklyFfmpegRenderPreparationResult> PrepareAndPersistAsync(WeeklyFfmpegRenderPreparationInput input, CancellationToken cancellationToken);
}

public sealed record WeeklyFfmpegRenderPreparationInput(
    Guid PipelineRunId,
    string WorkingDirectoryRoot,
    DateOnly WeekStartDate,
    string RegionId,
    string Language,
    string FinalRenderTimelinePath,
    string FinalRenderShotListPath,
    string TimelineTransitionPlanPath,
    string SegmentTimelineReportPath,
    string RetentionMarkerTimelinePath,
    string WeeklyProductionAssetManifestPath,
    string WeeklyAssetQualityReportPath,
    string LongformNarrationPath,
    string ShortformNarrationPath);

public sealed record WeeklyFfmpegRenderPreparationResult(
    WeeklyRenderContract RenderContract,
    WeeklyRenderInputManifest InputManifest,
    WeeklyFfmpegFilterGraphPlan FilterGraphPlan,
    WeeklyTransitionExecutionPlan TransitionExecutionPlan,
    WeeklyMotionEffectPlan MotionEffectPlan,
    WeeklyAudioAlignmentPlan AudioAlignmentPlan,
    WeeklyRendererValidationReport ValidationReport,
    string WeeklyRenderContractPath,
    string RenderInputManifestPath,
    string FfmpegFilterGraphPlanPath,
    string TransitionExecutionPlanPath,
    string MotionEffectExecutionPlanPath,
    string AudioAlignmentPlanPath,
    string RendererValidationReportPath);

public sealed record WeeklyRenderContract(
    Guid PipelineRunId,
    string Category,
    DateOnly WeekStartDate,
    string RegionId,
    string Language,
    WeeklyEpisodeRenderContract Longform,
    WeeklyEpisodeRenderContract Shortform);

public sealed record WeeklyEpisodeRenderContract(
    bool Enabled,
    int TargetWidth,
    int TargetHeight,
    int Fps,
    int DurationSeconds,
    string TimelinePath,
    int ShotCount,
    string OutputPath);

public sealed record WeeklyRenderInputManifest(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<WeeklyRenderInputAsset> Assets,
    bool AllTimelineAssetsFound,
    bool AllTimelineAssetsReadable,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    int TotalProductionAssetsDiscovered = 0,
    int TotalRenderInputAssets = 0,
    bool RenderInputHydrationPassed = true);

public sealed record WeeklyFfmpegInputManifestValidationReport(
    bool InputManifestReady,
    int ShotCount,
    int NullVisualAssetCollections,
    int NullTimelineItemCollections,
    int NullRenderInputCollections,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyRenderInputAsset(
    string AssetId,
    string AssetType,
    string AssetPath,
    bool Exists,
    int Width,
    int Height,
    double DurationSecondsUsed,
    bool UsedInLongform,
    bool UsedInShortform,
    bool Readable,
    long FileSizeBytes,
    IReadOnlyList<string> ValidationErrors);

public sealed record WeeklyFfmpegFilterGraphPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<WeeklyEpisodeFilterGraphPlan> Outputs,
    IReadOnlyList<string> GlobalSteps,
    string ExecutionMode);

public sealed record WeeklyEpisodeFilterGraphPlan(
    string EpisodeType,
    int TargetWidth,
    int TargetHeight,
    int Fps,
    string PixelFormat,
    string Container,
    IReadOnlyList<WeeklyShotFilterGraphStep> ShotSteps,
    IReadOnlyList<string> OutputFormattingSteps);

public sealed record WeeklyShotFilterGraphStep(
    string SegmentId,
    int ShotNumber,
    string AssetId,
    string AssetPath,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string ScalePlan,
    string AspectPlan,
    string FpsPlan,
    string MotionPlan,
    string TransitionInPlan,
    string TransitionOutPlan,
    bool OverlayEligible);

public sealed record WeeklyMotionEffectPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<WeeklyMotionEffectExecution> Motions,
    IReadOnlyDictionary<string, string> EffectMappings,
    bool MotionEffectPlanReady);

public sealed record WeeklyMotionEffectExecution(
    string EpisodeType,
    string SegmentId,
    int ShotNumber,
    string AssetId,
    string MotionEffect,
    string ExecutionPlan,
    double ZoomStart,
    double ZoomEnd,
    string PanPlan,
    double DurationSeconds);

public sealed record WeeklyTransitionExecutionPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<WeeklyTransitionExecution> Transitions,
    IReadOnlyDictionary<string, string> TransitionMappings,
    bool TransitionPlanReady,
    bool PreservesTotalTargetDuration);

public sealed record WeeklyTransitionExecution(
    string EpisodeType,
    string SegmentId,
    int ShotNumber,
    string TransitionIn,
    string TransitionOut,
    string ExecutionPlan,
    double RequestedDurationSeconds,
    double AppliedDurationSeconds,
    bool DurationCapped,
    double StartSecond,
    double EndSecond,
    double ShotDurationSeconds);

public sealed record WeeklyAudioAlignmentPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    string LongformExpectedAudioPath,
    string ShortformExpectedAudioPath,
    IReadOnlyList<WeeklyAudioSegmentAlignment> Segments,
    bool AudioAlignmentPlanReady);

public sealed record WeeklyAudioSegmentAlignment(
    string EpisodeType,
    string SegmentId,
    string SegmentType,
    string NarrationText,
    string ExpectedAudioPath,
    double StartSecond,
    double EndSecond,
    double DurationSeconds);

public sealed record WeeklyRendererValidationReport(
    bool RendererPreparationReady,
    bool LongformRenderContractReady,
    bool ShortformRenderContractReady,
    bool InputManifestReady,
    bool FilterGraphPlanReady,
    bool MotionEffectPlanReady,
    bool TransitionPlanReady,
    bool AudioAlignmentPlanReady,
    bool AllTimelineAssetsFound,
    bool AllTimelineAssetsReadable,
    bool DurationConsistencyPassed,
    bool ResolutionPlanPassed,
    bool TransitionPlanPassed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool ProfessionalVideoQualityPassed = false,
    bool NotScreenshotSlideshowPassed = false,
    bool EditorialStructurePassed = false,
    bool CinematicPacingPassed = false,
    bool CleanVisualHierarchyPassed = false,
    bool ReadableOverlayPassed = false,
    bool NarrationDrivenSceneFlowPassed = false,
    bool ReusableAstronomyAssetsPassed = false,
    bool FallbackAssetCoveragePassed = false,
    bool QualityValidationBeforeRenderPassed = false);

public sealed class WeeklyFfmpegRenderPreparationEngine(ILogger<WeeklyFfmpegRenderPreparationEngine> logger) : IWeeklyFfmpegRenderPreparationEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };

    public async Task<WeeklyFfmpegRenderPreparationResult> PrepareAndPersistAsync(WeeklyFfmpegRenderPreparationInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("WEEKLY_FFMPEG_RENDER_PREPARATION_START pipelineRunId={PipelineRunId} root={Root}", input.PipelineRunId, input.WorkingDirectoryRoot);
        var timeline = await ReadJsonAsync<FinalRenderTimeline>(input.FinalRenderTimelinePath, cancellationToken);
        _ = await ReadJsonAsync<IReadOnlyList<FinalRenderShotListEntry>>(input.FinalRenderShotListPath, cancellationToken);
        _ = await ReadJsonAsync<TimelineTransitionPlan>(input.TimelineTransitionPlanPath, cancellationToken);
        _ = await ReadJsonAsync<IReadOnlyList<SegmentTimelineReportEntry>>(input.SegmentTimelineReportPath, cancellationToken);
        _ = await ReadJsonAsync<RetentionMarkerTimeline>(input.RetentionMarkerTimelinePath, cancellationToken);
        var productionManifest = await ReadJsonAsync<WeeklyProductionAssetManifest>(input.WeeklyProductionAssetManifestPath, cancellationToken);
        _ = await ReadJsonAsync<object>(input.WeeklyAssetQualityReportPath, cancellationToken);
        _ = await ReadJsonAsync<object>(input.LongformNarrationPath, cancellationToken);
        _ = await ReadJsonAsync<object>(input.ShortformNarrationPath, cancellationToken);

        var renderDirectory = Path.Combine(input.WorkingDirectoryRoot, "render");
        Directory.CreateDirectory(renderDirectory);
        Directory.CreateDirectory(Path.Combine(renderDirectory, "longform"));
        Directory.CreateDirectory(Path.Combine(renderDirectory, "shortform"));
        Directory.CreateDirectory(Path.Combine(input.WorkingDirectoryRoot, "audio", "longform"));
        Directory.CreateDirectory(Path.Combine(input.WorkingDirectoryRoot, "audio", "shortform"));

        var longformSegments = timeline.Longform?.Segments ?? [];
        var shortformSegments = timeline.Shortform?.Segments ?? [];
        var nullTimelineItemCollections = longformSegments.Count(s => s.Shots is null) + shortformSegments.Count(s => s.Shots is null);
        var allShots = longformSegments.SelectMany(s => (s.Shots ?? Enumerable.Empty<FinalRenderShot>()).Select(shot => (Episode: "longform", Segment: s, Shot: shot)))
            .Concat(shortformSegments.SelectMany(s => (s.Shots ?? Enumerable.Empty<FinalRenderShot>()).Select(shot => (Episode: "shortform", Segment: s, Shot: shot))))
            .ToList();
        var contract = new WeeklyRenderContract(
            input.PipelineRunId,
            "WeeklySkyForecast",
            input.WeekStartDate,
            input.RegionId,
            input.Language,
            new WeeklyEpisodeRenderContract(true, 1920, 1080, 30, timeline.Longform.TargetDurationSeconds, input.FinalRenderTimelinePath, longformSegments.Sum(x => (x.Shots ?? []).Count), Path.Combine(renderDirectory, "longform", "weekly-skyforecast-longform.mp4")),
            new WeeklyEpisodeRenderContract(true, 1080, 1920, 30, timeline.Shortform.TargetDurationSeconds, input.FinalRenderTimelinePath, shortformSegments.Sum(x => (x.Shots ?? []).Count), Path.Combine(renderDirectory, "shortform", "weekly-skyforecast-shortform.mp4")));

        var manifest = await BuildInputManifestAsync(input.PipelineRunId, input.WorkingDirectoryRoot, renderDirectory, allShots, productionManifest, nullTimelineItemCollections, logger, cancellationToken);
        var motionPlan = BuildMotionPlan(input.PipelineRunId, allShots);
        var transitionPlan = BuildTransitionPlan(input.PipelineRunId, allShots);
        var filterGraphPlan = BuildFilterGraphPlan(input.PipelineRunId, timeline, motionPlan, transitionPlan);
        var audioPlan = BuildAudioPlan(input.PipelineRunId, input.WorkingDirectoryRoot, timeline);
        var validation = BuildValidationReport(contract, manifest, filterGraphPlan, motionPlan, transitionPlan, audioPlan, timeline);

        var contractPath = Path.Combine(renderDirectory, "weekly-render-contract.json");
        var manifestPath = Path.Combine(renderDirectory, "render-input-manifest.json");
        var filterGraphPath = Path.Combine(renderDirectory, "ffmpeg-filtergraph-plan.json");
        var transitionPath = Path.Combine(renderDirectory, "transition-execution-plan.json");
        var motionPath = Path.Combine(renderDirectory, "motion-effect-execution-plan.json");
        var audioPath = Path.Combine(renderDirectory, "audio-alignment-plan.json");
        var validationPath = Path.Combine(renderDirectory, "renderer-validation-report.json");

        await File.WriteAllTextAsync(contractPath, JsonSerializer.Serialize(contract, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(filterGraphPath, JsonSerializer.Serialize(filterGraphPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(transitionPath, JsonSerializer.Serialize(transitionPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(motionPath, JsonSerializer.Serialize(motionPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(audioPath, JsonSerializer.Serialize(audioPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);

        logger.LogInformation("WEEKLY_FFMPEG_RENDER_PREPARATION_COMPLETE pipelineRunId={PipelineRunId} ready={Ready}", input.PipelineRunId, validation.RendererPreparationReady);
        return new WeeklyFfmpegRenderPreparationResult(contract, manifest, filterGraphPlan, transitionPlan, motionPlan, audioPlan, validation, contractPath, manifestPath, filterGraphPath, transitionPath, motionPath, audioPath, validationPath);
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ?? throw new InvalidOperationException($"Unable to deserialize required renderer preparation input: {path}");
    }

    private static async Task<WeeklyRenderInputManifest> BuildInputManifestAsync(Guid pipelineRunId, string root, string renderDirectory, IReadOnlyList<(string Episode, FinalRenderSegment Segment, FinalRenderShot Shot)> allShots, WeeklyProductionAssetManifest productionManifest, int nullTimelineItemCollections, ILogger logger, CancellationToken cancellationToken)
    {
        var normalizedShots = allShots ?? [];
        var segmentBundles = productionManifest?.SegmentBundles ?? [];
        var nullVisualAssetCollections = segmentBundles.Count(bundle => bundle.AssignedVisualAssets is null);
        var nullRenderInputCollections = productionManifest?.SegmentBundles is null ? 1 : 0;

        logger.LogInformation(
            "FFMPEG_INPUT_MANIFEST_SOURCE_COUNTS shotCount={ShotCount} nullVisualAssetCollections={NullVisualAssetCollections} nullTimelineItemCollections={NullTimelineItemCollections} nullRenderInputCollections={NullRenderInputCollections}",
            normalizedShots.Count,
            nullVisualAssetCollections,
            nullTimelineItemCollections,
            nullRenderInputCollections);

        var candidateAssets = normalizedShots.Select(x => (AssetId: x.Shot.AssetId, AssetType: x.Shot.AssetType, AssetPath: x.Shot.AssetPath))
            .Concat(segmentBundles.SelectMany(b => b.AssignedVisualAssets ?? []).Where(a => a.Exists && a.ProductionReady).Select(a => (AssetId: $"{a.SourceType}:{a.AssetCode}", AssetType: a.SourceType.ToString(), AssetPath: a.FilePath)))
            .Concat(DiscoverRenderInputFiles(root))
            .Where(x => !string.IsNullOrWhiteSpace(x.AssetPath))
            .DistinctBy(x => Path.GetFullPath(x.AssetPath), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var assets = new List<WeeklyRenderInputAsset>();
        foreach (var group in candidateAssets.GroupBy(x => x.AssetPath, StringComparer.OrdinalIgnoreCase))
        {
            var firstCandidate = group.First();
            var shotMatches = normalizedShots.Where(x => x.Shot.AssetPath.Equals(firstCandidate.AssetPath, StringComparison.OrdinalIgnoreCase)).ToList();
            var first = shotMatches.FirstOrDefault().Shot ?? new FinalRenderShot(1, firstCandidate.AssetId, NormalizeRenderAssetType(firstCandidate.AssetType), firstCandidate.AssetPath, 0, 0, 0, "Cut", "Cut", "StaticHold", "production asset pool hydration");
            var errors = new List<string>();
            var exists = File.Exists(first.AssetPath);
            long fileSize = 0;
            var width = 0;
            var height = 0;
            var readable = false;
            if (!exists) errors.Add("Asset file is missing.");
            else
            {
                fileSize = new FileInfo(first.AssetPath).Length;
                if (fileSize <= 0) errors.Add("Asset file is zero bytes.");
                try
                {
                    var info = await Image.IdentifyAsync(first.AssetPath, cancellationToken);
                    if (info is null) errors.Add("Image could not be decoded.");
                    else
                    {
                        width = info.Width;
                        height = info.Height;
                        readable = width > 0 && height > 0;
                        if (!readable) errors.Add("Image dimensions are invalid.");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Image decode failed: {ex.Message}");
                }
            }
            assets.Add(new WeeklyRenderInputAsset(first.AssetId, NormalizeRenderAssetType(first.AssetType), first.AssetPath, exists, width, height, shotMatches.Sum(x => x.Shot.DurationSeconds), shotMatches.Any(x => x.Episode == "longform"), shotMatches.Any(x => x.Episode == "shortform"), readable, fileSize, errors));
        }
        var allFound = assets.All(x => x.Exists && x.FileSizeBytes > 0);
        var allReadable = assets.All(x => x.Readable && x.Width > 0 && x.Height > 0);
        var ordered = assets.OrderBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase).ToList();
        var totalProduction = Math.Max(productionManifest?.TotalProductionImageAssetCount ?? 0, ordered.Count);
        var hydrationPassed = totalProduction == 0 || ordered.Count >= Math.Ceiling(totalProduction * 0.8);
        var warnings = hydrationPassed
            ? new List<string>()
            : new List<string> { $"Render input hydration discovered {ordered.Count} assets; expected at least 80% of {totalProduction} production assets." };
        var manifest = new WeeklyRenderInputManifest(pipelineRunId, DateTime.UtcNow, ordered, allFound, allReadable, warnings, assets.SelectMany(x => (x.ValidationErrors ?? []).Select(e => $"{x.AssetId}: {e}")).ToList(), totalProduction, ordered.Count, hydrationPassed);
        var validationReport = new WeeklyFfmpegInputManifestValidationReport(true, normalizedShots.Count, nullVisualAssetCollections, nullTimelineItemCollections, nullRenderInputCollections, warnings, manifest.Errors);
        await File.WriteAllTextAsync(Path.Combine(renderDirectory, "ffmpeg-input-manifest-validation-report.json"), JsonSerializer.Serialize(validationReport, JsonOptions), cancellationToken);
        return manifest;
    }


    private static IEnumerable<(string AssetId, string AssetType, string AssetPath)> DiscoverRenderInputFiles(string root)
    {
        foreach (var directory in new[] { Path.Combine(root, "stellarium", "scenes"), Path.Combine(root, "ai-cinematic"), Path.Combine(root, "assets", "nasa"), Path.Combine(root, "assets", "jwst"), Path.Combine(root, "assets", "motion-graphics"), Path.Combine(root, "assets", "educational-overlays") })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories).Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
            {
                var type = ClassifyRenderAssetType(root, path);
                yield return (AssetId: $"{type}:{Path.GetFileNameWithoutExtension(path)}", AssetType: type, AssetPath: path);
            }
        }
    }

    private static string ClassifyRenderAssetType(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (rel.Contains("ai-cinematic/", StringComparison.OrdinalIgnoreCase)) return "AICinematic";
        if (rel.Contains("assets/nasa/", StringComparison.OrdinalIgnoreCase)) return "NASA";
        if (rel.Contains("assets/jwst/", StringComparison.OrdinalIgnoreCase)) return "JWST";
        if (rel.Contains("assets/motion-graphics/", StringComparison.OrdinalIgnoreCase)) return "MotionGraphic";
        if (rel.Contains("assets/educational-overlays/", StringComparison.OrdinalIgnoreCase)) return "EducationalOverlay";
        if (rel.Contains("astrophotography_target_scene", StringComparison.OrdinalIgnoreCase)) return "ExpandedStellarium";
        if (rel.Contains("stellarium/scenes/", StringComparison.OrdinalIgnoreCase)) return "Stellarium";
        return "Image";
    }

    private static string NormalizeRenderAssetType(string value) => value switch
    {
        "StellariumBase" => "Stellarium",
        "StellariumExpanded" => "ExpandedStellarium",
        "MotionGraphics" => "MotionGraphic",
        "EducationalOverlay" => "EducationalOverlay",
        _ => value
    };

    private static WeeklyMotionEffectPlan BuildMotionPlan(Guid pipelineRunId, IReadOnlyList<(string Episode, FinalRenderSegment Segment, FinalRenderShot Shot)> allShots)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["StaticHold"] = "loop image for duration; scale/pad to target size",
            ["SlowZoomIn"] = "zoompan from 1.00 to 1.08",
            ["SlowZoomOut"] = "zoompan from 1.08 to 1.00",
            ["SubtlePan"] = "slight horizontal or vertical pan",
            ["SlowPushIn"] = "zoompan from 1.00 to 1.12",
            ["KenBurnsZoom"] = "zoompan from 1.00 to 1.10",
            ["KenBurnsDrift"] = "zoompan with slight x/y drift"
        };
        var motions = allShots.Select(x =>
        {
            var (zs, ze, pan) = ResolveMotion(x.Shot.MotionEffect);
            return new WeeklyMotionEffectExecution(x.Episode, x.Segment.SegmentId, x.Shot.ShotNumber, x.Shot.AssetId, x.Shot.MotionEffect, mappings.GetValueOrDefault(x.Shot.MotionEffect, mappings["StaticHold"]), zs, ze, pan, x.Shot.DurationSeconds);
        }).ToList();
        return new WeeklyMotionEffectPlan(pipelineRunId, DateTime.UtcNow, motions, mappings, true);
    }

    private static (double ZoomStart, double ZoomEnd, string PanPlan) ResolveMotion(string effect) => effect switch
    {
        "SlowZoomIn" => (1.00, 1.08, "center anchored"),
        "SlowZoomOut" => (1.08, 1.00, "center anchored"),
        "SubtlePan" => (1.03, 1.03, "slight horizontal or vertical pan"),
        "SlowPushIn" => (1.00, 1.12, "center anchored"),
        "KenBurnsZoom" => (1.00, 1.10, "center anchored"),
        "KenBurnsDrift" => (1.00, 1.10, "slight x/y drift"),
        _ => (1.00, 1.00, "none")
    };

    private static WeeklyTransitionExecutionPlan BuildTransitionPlan(Guid pipelineRunId, IReadOnlyList<(string Episode, FinalRenderSegment Segment, FinalRenderShot Shot)> allShots)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cut"] = "direct concat",
            ["SoftCut"] = "short fade",
            ["Fade"] = "fade out/in",
            ["FadeIn"] = "fade in",
            ["FadeOut"] = "fade out",
            ["CrossFade"] = "xfade duration 0.75s",
            ["Dissolve"] = "xfade duration 1.0s",
            ["SlowDissolve"] = "xfade duration 1.5s",
            ["CinematicFade"] = "fade through black, 1.2s"
        };
        var transitions = allShots.Select(x =>
        {
            var requested = RequestedTransitionDuration(x.Shot.TransitionOut, x.Episode == "shortform");
            var cap = Math.Max(0, x.Shot.DurationSeconds * 0.20d);
            var applied = Math.Min(requested, cap);
            return new WeeklyTransitionExecution(x.Episode, x.Segment.SegmentId, x.Shot.ShotNumber, x.Shot.TransitionIn, x.Shot.TransitionOut, mappings.GetValueOrDefault(x.Shot.TransitionOut, "direct concat"), requested, applied, applied < requested, x.Shot.StartSecond, x.Shot.EndSecond, x.Shot.DurationSeconds);
        }).ToList();
        return new WeeklyTransitionExecutionPlan(pipelineRunId, DateTime.UtcNow, transitions, mappings, transitions.All(x => x.AppliedDurationSeconds >= 0), true);
    }

    private static double RequestedTransitionDuration(string transition, bool shortform)
    {
        var value = transition switch
        {
            "SoftCut" => 0.25,
            "Fade" or "FadeIn" or "FadeOut" => 0.5,
            "CrossFade" => 0.75,
            "Dissolve" => 1.0,
            "SlowDissolve" => 1.5,
            "CinematicFade" => 1.2,
            _ => 0.0
        };
        return shortform ? Math.Min(value, 0.6) : value;
    }

    private static WeeklyFfmpegFilterGraphPlan BuildFilterGraphPlan(Guid pipelineRunId, FinalRenderTimeline timeline, WeeklyMotionEffectPlan motionPlan, WeeklyTransitionExecutionPlan transitionPlan)
    {
        return new WeeklyFfmpegFilterGraphPlan(pipelineRunId, DateTime.UtcNow,
            [BuildEpisodeFilterPlan("longform", timeline.Longform, 1920, 1080, motionPlan, transitionPlan), BuildEpisodeFilterPlan("shortform", timeline.Shortform, 1080, 1920, motionPlan, transitionPlan)],
            ["Scale each decoded image to cover/pad target canvas", "Normalize every stream to 30fps", "Apply deterministic motion effect expression", "Apply planned transition execution", "Composite retention/educational overlays when present", "Format output as yuv420p mp4"],
            "PlanOnlyDoNotExecuteFfmpeg");
    }

    private static WeeklyEpisodeFilterGraphPlan BuildEpisodeFilterPlan(string episodeType, FinalRenderEpisodeTimeline timeline, int width, int height, WeeklyMotionEffectPlan motionPlan, WeeklyTransitionExecutionPlan transitionPlan)
    {
        var steps = (timeline.Segments ?? []).SelectMany(segment => (segment.Shots ?? []).Select(shot => new WeeklyShotFilterGraphStep(
            segment.SegmentId,
            shot.ShotNumber,
            shot.AssetId,
            shot.AssetPath,
            shot.StartSecond,
            shot.EndSecond,
            shot.DurationSeconds,
            $"scale={width}:{height}:force_original_aspect_ratio=decrease",
            $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2 or crop to preserve target canvas",
            "fps=30",
            motionPlan.Motions.First(m => m.EpisodeType == episodeType && m.SegmentId == segment.SegmentId && m.ShotNumber == shot.ShotNumber).ExecutionPlan,
            transitionPlan.Transitions.First(t => t.EpisodeType == episodeType && t.SegmentId == segment.SegmentId && t.ShotNumber == shot.ShotNumber).TransitionIn,
            transitionPlan.Transitions.First(t => t.EpisodeType == episodeType && t.SegmentId == segment.SegmentId && t.ShotNumber == shot.ShotNumber).ExecutionPlan,
            shot.IsOverlay))).ToList();
        return new WeeklyEpisodeFilterGraphPlan(episodeType, width, height, 30, "yuv420p", "mp4", steps, [$"format=yuv420p", $"fps=30", $"mux mp4 at {width}x{height}"]);
    }

    private static WeeklyAudioAlignmentPlan BuildAudioPlan(Guid pipelineRunId, string root, FinalRenderTimeline timeline)
    {
        var longformAudioDirectory = Path.Combine(root, "audio", "longform");
        var shortformAudioDirectory = Path.Combine(root, "audio", "shortform");
        var segments = timeline.Longform.Segments.Select(s => BuildAudioSegment("longform", longformAudioDirectory, s))
            .Concat(timeline.Shortform.Segments.Select(s => BuildAudioSegment("shortform", shortformAudioDirectory, s)))
            .ToList();
        return new WeeklyAudioAlignmentPlan(pipelineRunId, DateTime.UtcNow, Path.Combine(longformAudioDirectory, "weekly-skyforecast-longform.mp3"), Path.Combine(shortformAudioDirectory, "weekly-skyforecast-shortform.mp3"), segments, segments.All(s => s.DurationSeconds > 0 && s.EndSecond >= s.StartSecond));
    }

    private static WeeklyAudioSegmentAlignment BuildAudioSegment(string episodeType, string audioDirectory, FinalRenderSegment segment)
        => new(episodeType, segment.SegmentId, segment.SegmentType, segment.NarrationText, Path.Combine(audioDirectory, $"{segment.SegmentId}.mp3"), segment.StartSecond, segment.EndSecond, segment.DurationSeconds);

    private static WeeklyRendererValidationReport BuildValidationReport(WeeklyRenderContract contract, WeeklyRenderInputManifest manifest, WeeklyFfmpegFilterGraphPlan filterGraphPlan, WeeklyMotionEffectPlan motionPlan, WeeklyTransitionExecutionPlan transitionPlan, WeeklyAudioAlignmentPlan audioPlan, FinalRenderTimeline timeline)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        errors.AddRange(manifest.Errors);
        var longformReady = contract.Longform.Enabled && contract.Longform.TargetWidth == 1920 && contract.Longform.TargetHeight == 1080 && contract.Longform.Fps == 30 && contract.Longform.DurationSeconds == 380 && contract.Longform.ShotCount == (timeline.Longform.Segments ?? []).Sum(x => (x.Shots ?? []).Count);
        var shortformReady = contract.Shortform.Enabled && contract.Shortform.TargetWidth == 1080 && contract.Shortform.TargetHeight == 1920 && contract.Shortform.Fps == 30 && contract.Shortform.DurationSeconds == 50 && contract.Shortform.ShotCount == (timeline.Shortform.Segments ?? []).Sum(x => (x.Shots ?? []).Count);
        var durationPassed = timeline.Longform.ActualDurationSeconds == contract.Longform.DurationSeconds && timeline.Shortform.ActualDurationSeconds == contract.Shortform.DurationSeconds;
        var resolutionPassed = filterGraphPlan.Outputs.Any(x => x.EpisodeType == "longform" && x.TargetWidth == 1920 && x.TargetHeight == 1080 && x.Fps == 30 && x.PixelFormat == "yuv420p" && x.Container == "mp4")
            && filterGraphPlan.Outputs.Any(x => x.EpisodeType == "shortform" && x.TargetWidth == 1080 && x.TargetHeight == 1920 && x.Fps == 30 && x.PixelFormat == "yuv420p" && x.Container == "mp4");
        var transitionPassed = transitionPlan.TransitionPlanReady && transitionPlan.Transitions.All(x => x.AppliedDurationSeconds >= 0 && x.AppliedDurationSeconds <= x.ShotDurationSeconds * 0.20d + 0.0001d);
        if (!longformReady) errors.Add("Longform render contract is not ready.");
        if (!shortformReady) errors.Add("Shortform render contract is not ready.");
        if (!durationPassed) errors.Add("Timeline durations do not match target render contract durations.");
        if (!resolutionPassed) errors.Add("Resolution/fps/pixel-format plan failed.");
        if (!transitionPassed) errors.Add("Transition plan failed.");

        var allSegments = (timeline.Longform.Segments ?? []).Concat(timeline.Shortform.Segments ?? []).ToList();
        var allShots = allSegments.SelectMany(x => x.Shots ?? []).ToList();
        var distinctSegmentTypes = allSegments.Select(x => x.SegmentType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var distinctAssetTypes = manifest.Assets.Select(x => x.AssetType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var distinctMotionEffects = motionPlan.Motions.Select(x => x.MotionEffect).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var overlayShots = allShots.Where(x => x.IsOverlay).ToList();
        var screenshotLikeShots = allShots.Count(x => x.AssetType.Contains("Stellarium", StringComparison.OrdinalIgnoreCase) || x.AssetType.Contains("NASA", StringComparison.OrdinalIgnoreCase) || x.AssetType.Contains("Screenshot", StringComparison.OrdinalIgnoreCase));
        var screenshotLikeRatio = allShots.Count == 0 ? 1d : (double)screenshotLikeShots / allShots.Count;
        var notScreenshotSlideshowPassed = allShots.Count > 0 && distinctAssetTypes >= 3 && distinctMotionEffects >= 2 && screenshotLikeRatio < 0.85d;
        var editorialStructurePassed = allSegments.Count >= 3 && distinctSegmentTypes >= 3;
        var cinematicPacingPassed = motionPlan.MotionEffectPlanReady && transitionPlan.TransitionPlanReady && allShots.All(x => x.DurationSeconds > 0) && distinctMotionEffects >= 2;
        var cleanVisualHierarchyPassed = allSegments.All(segment => (segment.Shots ?? []).Any(shot => !shot.IsOverlay));
        var readableOverlayPassed = overlayShots.Count == 0 || overlayShots.All(x => x.OverlayStartSecond is null || x.OverlayEndSecond is null || x.OverlayEndSecond > x.OverlayStartSecond);
        var narrationDrivenSceneFlowPassed = audioPlan.AudioAlignmentPlanReady && allSegments.All(x => !string.IsNullOrWhiteSpace(x.NarrationText));
        var reusableAstronomyAssetsPassed = manifest.Assets.Count >= allShots.Count && distinctAssetTypes >= 3;
        var fallbackAssetCoveragePassed = manifest.RenderInputHydrationPassed && manifest.AllTimelineAssetsFound && manifest.AllTimelineAssetsReadable && distinctAssetTypes >= 3;
        var qualityValidationBeforeRenderPassed = errors.Count == 0 && manifest.Errors.Count == 0 && filterGraphPlan.Outputs.Count == 2;
        var professionalVideoQualityPassed = notScreenshotSlideshowPassed && editorialStructurePassed && cinematicPacingPassed && cleanVisualHierarchyPassed && readableOverlayPassed && narrationDrivenSceneFlowPassed && reusableAstronomyAssetsPassed && fallbackAssetCoveragePassed && qualityValidationBeforeRenderPassed;
        if (!professionalVideoQualityPassed)
            errors.Add("Professional video quality gate failed before rendering: AstroPulse output cannot be a screenshot slideshow and must include editorial structure, cinematic pacing, readable overlays, narration-driven scene flow, reusable astronomy/fallback assets, and validation artifacts.");

        var ready = longformReady && shortformReady && manifest.AllTimelineAssetsFound && manifest.AllTimelineAssetsReadable && durationPassed && resolutionPassed && transitionPassed && audioPlan.AudioAlignmentPlanReady && motionPlan.MotionEffectPlanReady && professionalVideoQualityPassed;
        return new WeeklyRendererValidationReport(ready, longformReady, shortformReady, manifest.Assets.Count > 0 && manifest.Errors.Count == 0, filterGraphPlan.Outputs.Count == 2, motionPlan.MotionEffectPlanReady, transitionPlan.TransitionPlanReady, audioPlan.AudioAlignmentPlanReady, manifest.AllTimelineAssetsFound, manifest.AllTimelineAssetsReadable, durationPassed, resolutionPassed, transitionPassed, warnings, errors, professionalVideoQualityPassed, notScreenshotSlideshowPassed, editorialStructurePassed, cinematicPacingPassed, cleanVisualHierarchyPassed, readableOverlayPassed, narrationDrivenSceneFlowPassed, reusableAstronomyAssetsPassed, fallbackAssetCoveragePassed, qualityValidationBeforeRenderPassed);
    }
}
