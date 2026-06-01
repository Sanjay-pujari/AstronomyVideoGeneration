using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;

public interface IWeeklyExistingRunVideoRenderer
{
    Task<WeeklyExistingRunRenderResponse> RenderAsync(Guid pipelineRunId, WeeklyExistingRunRenderRequest request, CancellationToken cancellationToken);
}

public sealed record WeeklyExistingRunRenderRequest(
    bool RenderLongform = true,
    bool RenderShortform = true,
    bool OverwriteExisting = false,
    bool DryRun = false,
    bool DebugStoryboard = false,
    bool AllowSilent = true,
    bool? UseStagedRendering = null,
    bool UseAudioDrivenTimeline = false,
    bool MergeAudio = false);

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
    bool RenderVisualSelectionReady,
    bool RenderVisualDiversityReady,
    string ResolvedRenderShotPlanPath,
    string RenderVisualSelectionReportPath,
    string RenderDiversityValidationReportPath,
    bool RenderInputHydrationPassed,
    int TotalProductionAssetsDiscovered,
    int TotalRenderInputAssets,
    string RenderStoryboardReportPath,
    string ResolvedPipelineRunRoot,
    int HeroEventWesternGroupingFrameCount,
    int PlanetHighlightsWesternGroupingFrameCount,
    int ShortformWesternGroupingFrameCount,
    bool MoonOnlyStellariumDetected,
    double MaxLongformShotDurationSeconds,
    double MaxShortformShotDurationSeconds,
    int RepeatedAssetPathCount,
    string AssetRepeatValidationMode,
    bool AssetRepeatWeightedValidationPassed,
    bool AssetFamilyDistributionPassed,
    int SameAssetPathHardFailureCount,
    int SameAssetPathWarningCount,
    int SameAssetPathMaxUsageCount,
    double SameAssetPathMaxDurationPercent,
    int MaxConsecutiveSameAssetPathCount,
    bool AiCinematicDiversityPassed,
    bool MotionGraphicDiversityPassed,
    bool StellariumSceneBalancePassed,
    bool ShortformPacingPassed,
    bool LongformPacingPassed,
    bool VisualDistributionPassed,
    int NasaAssetUsageCount,
    int JwstAssetUsageCount,
    int MotionGraphicUsageCount,
    int AiCinematicDistinctUsageCount,
    double MoonHeroVisualDurationPercent,
    bool AudioRequired,
    bool AudioFound,
    bool AudioAttached,
    bool RenderedSilent,
    bool DebugStoryboardRendered,
    string LongformDebugVideoPath,
    string ShortformDebugVideoPath,
    bool UseStagedRendering,
    string RenderMode,
    int LongformClipCount,
    int LongformClipsRendered,
    int ShortformClipCount,
    int ShortformClipsRendered,
    string TransitionMode,
    bool VideoOnlyRenderReady,
    bool LongformVideoOnlyQualityReady,
    bool ShortformVideoOnlyQualityReady,
    bool LongformRepetitionPolishPassed,
    bool ShortformVerticalLayoutPassed,
    bool ShortformSafeAreaPassed,
    string LongformRenderMode,
    string ShortformRenderMode,
    int ShortformContainLayoutCount,
    int ShortformSmartCropLayoutCount,
    int ShortformCroppedTextRiskCount,
    int LongformBackToBackRepeatCount,
    int LongformSameAssetSameSegmentRepeatCount,
    int LongformSameFamilyConsecutiveMax,
    int? FfmpegExitCode,
    string FfmpegStderrPath,
    string FailedCommandPath,
    string FailedStage,
    int? FailedShotNumber,
    IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> PlannedCommands,
    bool FinalVideoRenderReady,
    bool AudioVideoMergeReady,
    bool LongformFinalRendered,
    bool ShortformFinalRendered,
    string LongformFinalVideoPath,
    string ShortformFinalVideoPath,
    bool LongformAudioAttached,
    bool ShortformAudioAttached,
    double LongformFinalDurationSeconds,
    double ShortformFinalDurationSeconds,
    double LongformDurationDeltaSeconds,
    double ShortformDurationDeltaSeconds,
    string FinalAudioVideoMergeReportPath,
    string FinalRenderReportPath,
    bool UseAudioDrivenTimeline,
    bool AudioDrivenRenderReady,
    string AudioDrivenRenderValidationReportPath,
    bool AudioDrivenTimelineLoaded,
    bool AudioDrivenShotPlanLoaded,
    bool AudioDrivenRenderContractLoaded,
    bool LongformAudioFound,
    bool ShortformAudioFound,
    bool LongformVideoOnlyExists,
    bool ShortformVideoOnlyExists,
    bool LongformVideoOnlyRendered,
    bool ShortformVideoOnlyRendered,
    double LongformAudioDurationSeconds,
    double ShortformAudioDurationSeconds,
    double LongformVideoDurationSeconds,
    double ShortformVideoDurationSeconds,
    IReadOnlyList<string> AudioDrivenValidationErrors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);


public sealed record WeeklyAudioDrivenRenderValidationReport(
    Guid PipelineRunId,
    bool AudioDrivenRenderReady,
    bool AudioDrivenTimelineLoaded,
    bool AudioDrivenShotPlanLoaded,
    bool AudioDrivenRenderContractLoaded,
    bool LongformAudioFound,
    bool ShortformAudioFound,
    bool LongformVideoOnlyExists,
    bool ShortformVideoOnlyExists,
    bool LongformVideoOnlyRendered,
    bool ShortformVideoOnlyRendered,
    double LongformAudioDurationSeconds,
    double ShortformAudioDurationSeconds,
    double LongformVideoDurationSeconds,
    double ShortformVideoDurationSeconds,
    double LongformDurationDeltaSeconds,
    double ShortformDurationDeltaSeconds,
    bool AudioDrivenShotDurationsValid,
    bool AudioDrivenNoGaps,
    bool AudioDrivenNoOverlaps,
    double LongformExpectedDurationSeconds,
    double ShortformExpectedDurationSeconds,
    IReadOnlyList<string> AudioDrivenValidationErrors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyFinalAudioVideoMergeReport(
    Guid PipelineRunId,
    bool AudioVideoMergeReady,
    WeeklyFinalAudioVideoMergeEpisodeReport Longform,
    WeeklyFinalAudioVideoMergeEpisodeReport Shortform,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyFinalAudioVideoMergeEpisodeReport(
    bool Requested,
    string VideoOnlyPath,
    string AudioPath,
    string FinalVideoPath,
    double VideoDurationSeconds,
    double AudioDurationSeconds,
    double DurationDeltaSeconds,
    bool AudioAttached,
    bool HasAudioStream,
    bool HasVideoStream,
    bool Merged);

public sealed record WeeklyFinalRenderReport(
    Guid PipelineRunId,
    DateTime RenderStartedAtUtc,
    DateTime RenderCompletedAtUtc,
    bool FinalVideoRenderReady,
    bool AudioVideoMergeReady,
    WeeklyFinalAudioVideoMergeEpisodeReport Longform,
    WeeklyFinalAudioVideoMergeEpisodeReport Shortform,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyExistingRunVideoRenderReport(
    Guid PipelineRunId,
    DateTime RenderStartedAtUtc,
    DateTime RenderCompletedAtUtc,
    bool DryRun,
    WeeklyExistingRunEpisodeRenderReport Longform,
    WeeklyExistingRunEpisodeRenderReport Shortform,
    string RenderMode,
    bool UseStagedRendering,
    bool DebugStoryboard,
    int LongformClipCount,
    int ShortformClipCount,
    int LongformClipsRendered,
    int ShortformClipsRendered,
    string TransitionMode,
    int FfmpegCommandLength,
    string FfmpegStderrPath,
    string? FailedStage,
    int? FailedShotNumber,
    string LongformRenderMode,
    string ShortformRenderMode,
    bool VideoOnlyRenderReady,
    bool LongformVideoOnlyQualityReady,
    bool ShortformVideoOnlyQualityReady,
    bool LongformRepetitionPolishPassed,
    bool ShortformVerticalLayoutPassed,
    bool ShortformSafeAreaPassed,
    int ShortformContainLayoutCount,
    int ShortformSmartCropLayoutCount,
    int ShortformCroppedTextRiskCount,
    int LongformBackToBackRepeatCount,
    int LongformSameAssetSameSegmentRepeatCount,
    int LongformSameFamilyConsecutiveMax,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyExistingRunEpisodeRenderReport(
    bool Requested,
    bool Rendered,
    bool Skipped,
    string OutputPath,
    double DurationSeconds,
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
    bool DebugStoryboard,
    string Command,
    IReadOnlyList<string> SegmentFiles,
    IReadOnlyList<string> Arguments,
    bool UseStagedRendering,
    string RenderMode,
    string TempDirectory,
    string ClipsDirectory,
    string StderrPath,
    int CommandLength,
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
    bool VisualDistributionPassed,
    int ContainLayoutCount,
    int SmartCropLayoutCount,
    int CroppedTextRiskCount);

public sealed record WeeklyRenderQualityReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    double MaxLongformShotDurationSeconds,
    double MaxShortformShotDurationSeconds,
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
    bool LongformVideoOnlyQualityReady,
    bool ShortformVideoOnlyQualityReady,
    bool VideoOnlyRenderReady,
    bool LongformRepetitionPolishPassed,
    bool ShortformVerticalLayoutPassed,
    bool ShortformSafeAreaPassed,
    int ShortformContainLayoutCount,
    int ShortformSmartCropLayoutCount,
    int ShortformCroppedTextRiskCount,
    int LongformBackToBackRepeatCount,
    int LongformSameAssetSameSegmentRepeatCount,
    int LongformSameFamilyConsecutiveMax,
    string LongformRenderMode,
    string ShortformRenderMode,
    IReadOnlyList<WeeklyExistingRunEpisodeQualityMetrics> EpisodeMetrics,
    IReadOnlyList<string> Warnings);

public sealed record ResolvedRenderShotPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<ResolvedRenderEpisodeShotPlan> Episodes);

public sealed record ResolvedRenderEpisodeShotPlan(
    string EpisodeType,
    double ActualDurationSeconds,
    IReadOnlyList<ResolvedRenderSegmentShotPlan> Segments);

public sealed record ResolvedRenderSegmentShotPlan(
    string EpisodeType,
    string SegmentId,
    string SegmentType,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    IReadOnlyList<ResolvedRenderShotPlanEntry> Shots);

public sealed record ResolvedRenderShotPlanEntry(
    int ShotNumber,
    string AssetId,
    string AssetType,
    string AssetPath,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string TransitionIn,
    string TransitionOut,
    string MotionEffect,
    string Purpose,
    string LayoutMode,
    bool VerticalVariantUsed,
    bool VerticalFallbackContainUsed);

public sealed record RenderVisualSelectionReport(
    int HeroEventWesternGroupingFrameCount,
    int PlanetHighlightsWesternGroupingFrameCount,
    int ShortformWesternGroupingFrameCount,
    int MoonHeroFrameCount,
    int ExpandedAstrophotographyFrameCount,
    bool MoonOnlyStellariumDetected,
    double MaxLongformShotDurationSeconds,
    double MaxShortformShotDurationSeconds,
    bool SameAssetRepeatedTooMuch,
    int RepeatedAssetPathCount,
    string AssetRepeatValidationMode,
    int MaxAllowedSameAssetPathUsesLongform,
    int MaxAllowedSameAssetPathUsesShortform,
    int SameAssetPathHardFailureCount,
    int SameAssetPathWarningCount,
    int SameAssetPathMaxUsageCount,
    double SameAssetPathMaxDurationPercent,
    int MaxConsecutiveSameAssetPathCount,
    IReadOnlyDictionary<string, double> FamilyDurationPercentages,
    bool AssetRepeatLimitPassed,
    bool AssetFamilyDistributionPassed,
    bool RenderVisualDiversityReady,
    int WeeklyOverviewTimelineUsageCount,
    int FastCinematicSkyHookUsageCount,
    bool AiCinematicDiversityPassed,
    bool MotionGraphicDiversityPassed,
    bool StellariumSceneBalancePassed,
    bool ShortformPacingPassed,
    bool LongformPacingPassed,
    bool VisualDistributionPassed,
    int NasaAssetUsageCount,
    int JwstAssetUsageCount,
    int MotionGraphicUsageCount,
    int AiCinematicDistinctUsageCount,
    double MoonHeroVisualDurationPercent,
    IReadOnlyDictionary<string, int> AssetPathUsageCount,
    IReadOnlyDictionary<string, int> AiCinematicAssetUsageCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record RenderDiversityValidationReport(
    bool RenderVisualDiversityReady,
    bool SegmentAwareAssetResolutionPassed,
    bool HeroEventGroupingFramesPassed,
    bool PlanetHighlightsGroupingFramesPassed,
    bool MoonOnlyDetectionPassed,
    bool AssetRepeatLimitPassed,
    string AssetRepeatValidationMode,
    bool AssetRepeatWeightedValidationPassed,
    bool AssetFamilyDistributionPassed,
    int SameAssetPathHardFailureCount,
    int SameAssetPathWarningCount,
    int MaxConsecutiveSameAssetPathCount,
    bool AiAssetDiversityPassed,
    bool MotionGraphicDiversityPassed,
    bool ShotDurationLimitPassed,
    bool ShortformPacingPassed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record RenderStoryboardReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<RenderStoryboardSegmentReport> Segments);

public sealed record RenderStoryboardSegmentReport(
    string EpisodeType,
    string SegmentType,
    double StartSecond,
    double EndSecond,
    string NarrationExcerpt,
    IReadOnlyList<RenderStoryboardShotReport> Shots);

public sealed record RenderStoryboardShotReport(
    int ShotNumber,
    string AssetType,
    string AssetCode,
    string SceneFamily,
    double DurationSeconds,
    string ReasonSelected);

public sealed class WeeklyExistingRunVideoRenderer(
    IOptions<RenderingOptions> renderingOptions,
    IWeeklyPipelineRunDirectoryResolver pipelineRunDirectoryResolver,
    ILogger<WeeklyExistingRunVideoRenderer> logger) : IWeeklyExistingRunVideoRenderer
{
    private const int ShortformCanvasWidth = 1080;
    private const int ShortformCanvasHeight = 1920;
    private const int ShortformSafeMarginX = 80;
    private const int ShortformSafeMarginTop = 220;
    private const int ShortformSafeMarginBottom = 260;
    private const int ShortformSafeContentWidth = ShortformCanvasWidth - (ShortformSafeMarginX * 2);
    private const int ShortformSafeContentHeight = ShortformCanvasHeight - ShortformSafeMarginTop - ShortformSafeMarginBottom;
    private const double FinalAudioVideoDurationToleranceSeconds = 0.5d;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private readonly RenderingOptions _renderingOptions = renderingOptions.Value;

    public async Task<WeeklyExistingRunRenderResponse> RenderAsync(Guid pipelineRunId, WeeklyExistingRunRenderRequest request, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        logger.LogInformation("WEEKLY_RENDER_EXISTING_RUN_START pipelineRunId={PipelineRunId} dryRun={DryRun} renderLongform={RenderLongform} renderShortform={RenderShortform}", pipelineRunId, request.DryRun, request.RenderLongform, request.RenderShortform);
        if (request.UseAudioDrivenTimeline && request.MergeAudio)
        {
            logger.LogInformation("WEEKLY_FINAL_RENDER_START pipelineRunId={PipelineRunId} dryRun={DryRun} renderLongform={RenderLongform} renderShortform={RenderShortform}", pipelineRunId, request.DryRun, request.RenderLongform, request.RenderShortform);
        }

        var warnings = new List<string>();
        var errors = new List<string>();
        var commandReports = new List<WeeklyExistingRunFfmpegCommandReport>();
        var commandPlans = new List<WeeklyExistingRunFfmpegCommandPlan>();

        try
        {
            var root = await pipelineRunDirectoryResolver.ResolveRunDirectoryAsync(pipelineRunId);
            var renderDirectory = Path.Combine(root, "render");
            var paths = WeeklyExistingRunRequiredPaths.FromRoot(root, request.UseAudioDrivenTimeline);
            var loaded = await LoadInputsAsync(paths, cancellationToken);
            var hydration = await HydrateRenderInputManifestAsync(pipelineRunId, root, loaded.Manifest, loaded.ProductionAssetManifest, loaded.Timeline, cancellationToken);
            loaded = loaded with { Manifest = hydration.Manifest };
            await File.WriteAllTextAsync(paths.InputManifest, JsonSerializer.Serialize(loaded.Manifest, JsonOptions), cancellationToken);
            logger.LogInformation("WEEKLY_RENDER_INPUTS_LOADED pipelineRunId={PipelineRunId} root={Root} productionAssets={ProductionAssets} renderInputAssets={RenderInputAssets}", pipelineRunId, root, hydration.TotalProductionAssetsDiscovered, hydration.TotalRenderInputAssets);
            if (request.UseAudioDrivenTimeline && request.MergeAudio)
            {
                logger.LogInformation("WEEKLY_FINAL_AUDIO_DRIVEN_TIMELINE_LOADED pipelineRunId={PipelineRunId} timelinePath={TimelinePath}", pipelineRunId, paths.FinalTimeline);
            }

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
            Directory.CreateDirectory(Path.Combine(renderDirectory, "temp", "final", "longform"));
            Directory.CreateDirectory(Path.Combine(renderDirectory, "temp", "final", "shortform"));
            logger.LogInformation("WEEKLY_RENDER_VALIDATION_PASSED pipelineRunId={PipelineRunId}", pipelineRunId);

            var finalLongformOutput = Path.Combine(renderDirectory, "longform", request.DebugStoryboard ? "weekly-skyforecast-longform-final-debug.mp4" : "weekly-skyforecast-longform-final.mp4");
            var finalShortformOutput = Path.Combine(renderDirectory, "shortform", request.DebugStoryboard ? "weekly-skyforecast-shortform-final-debug.mp4" : "weekly-skyforecast-shortform-final.mp4");
            var videoOnlyLongformOutput = Path.Combine(renderDirectory, "temp", "final", "longform", request.DebugStoryboard ? "weekly-skyforecast-longform-video-only-debug.mp4" : "weekly-skyforecast-longform-video-only.mp4");
            var videoOnlyShortformOutput = Path.Combine(renderDirectory, "temp", "final", "shortform", request.DebugStoryboard ? "weekly-skyforecast-shortform-video-only-debug.mp4" : "weekly-skyforecast-shortform-video-only.mp4");
            var productionLongformOutput = NormalizeOutputPath(loaded.Contract.Longform.OutputPath, Path.Combine(renderDirectory, "longform", "weekly-skyforecast-longform.mp4"));
            var productionShortformOutput = NormalizeOutputPath(loaded.Contract.Shortform.OutputPath, Path.Combine(renderDirectory, "shortform", "weekly-skyforecast-shortform.mp4"));
            var longformOutput = request.MergeAudio ? videoOnlyLongformOutput : (request.DebugStoryboard ? Path.Combine(renderDirectory, "longform", "weekly-skyforecast-longform-debug.mp4") : productionLongformOutput);
            var shortformOutput = request.MergeAudio ? videoOnlyShortformOutput : (request.DebugStoryboard ? Path.Combine(renderDirectory, "shortform", "weekly-skyforecast-shortform-debug.mp4") : productionShortformOutput);
            var videoReportPath = request.MergeAudio ? Path.Combine(renderDirectory, "final-render-video-only-report.json") : Path.Combine(renderDirectory, "video-render-report.json");
            var ffmpegReportPath = Path.Combine(renderDirectory, "ffmpeg-execution-report.json");
            var qualityReportPath = Path.Combine(renderDirectory, "render-quality-report.json");
            var resolvedShotPlanPath = request.UseAudioDrivenTimeline
                ? Path.Combine(renderDirectory, "audio-driven-resolved-render-shot-plan.json")
                : Path.Combine(renderDirectory, "resolved-render-shot-plan.json");
            var visualSelectionReportPath = Path.Combine(renderDirectory, "render-visual-selection-report.json");
            var diversityValidationReportPath = Path.Combine(renderDirectory, "render-diversity-validation-report.json");
            var audioDrivenRenderValidationReportPath = Path.Combine(renderDirectory, "audio-driven-render-validation-report.json");
            var storyboardReportPath = request.UseAudioDrivenTimeline
                ? Path.Combine(renderDirectory, "audio-driven-render-storyboard-report.json")
                : Path.Combine(renderDirectory, "render-storyboard-report.json");
            var commandPlanPath = Path.Combine(renderDirectory, "logs", "ffmpeg-command-plan.json");
            var longformAudioPath = Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3");
            var shortformAudioPath = Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3");
            var longformAudioFound = File.Exists(longformAudioPath);
            var shortformAudioFound = File.Exists(shortformAudioPath);

            var skipLongformFinal = false;
            var skipShortformFinal = false;
            if (request.MergeAudio && !request.DryRun)
            {
                (skipLongformFinal, skipShortformFinal) = await PrepareFinalRenderOutputsAsync(
                    pipelineRunId,
                    request,
                    videoOnlyLongformOutput,
                    videoOnlyShortformOutput,
                    finalLongformOutput,
                    finalShortformOutput,
                    cancellationToken);
            }

            var longformResult = WeeklyExistingRunEpisodeRenderReportFactory.NotRequested(longformOutput);
            var shortformResult = WeeklyExistingRunEpisodeRenderReportFactory.NotRequested(shortformOutput);
            if (skipLongformFinal)
            {
                var info = new FileInfo(finalLongformOutput);
                longformResult = new WeeklyExistingRunEpisodeRenderReport(true, false, true, finalLongformOutput, 0, info.Length, true);
            }
            if (skipShortformFinal)
            {
                var info = new FileInfo(finalShortformOutput);
                shortformResult = new WeeklyExistingRunEpisodeRenderReport(true, false, true, finalShortformOutput, 0, info.Length, true);
            }

            if (request.RenderLongform && !skipLongformFinal)
            {
                commandPlans.Add(await BuildCommandPlanAsync("longform", loaded.Contract.Longform, loaded.Timeline.Longform, loaded.Manifest, loaded.ProductionAssetManifest, loaded.AudioPlan.LongformExpectedAudioPath, longformOutput, request, warnings, cancellationToken));
            }
            if (request.RenderShortform && !skipShortformFinal)
            {
                commandPlans.Add(await BuildCommandPlanAsync("shortform", loaded.Contract.Shortform, loaded.Timeline.Shortform, loaded.Manifest, loaded.ProductionAssetManifest, loaded.AudioPlan.ShortformExpectedAudioPath, shortformOutput, request, warnings, cancellationToken));
            }
            await File.WriteAllTextAsync(commandPlanPath, JsonSerializer.Serialize(commandPlans, JsonOptions), cancellationToken);

            RenderVisualSelectionReport visualSelectionReport;
            RenderDiversityValidationReport diversityValidationReport;
            WeeklyAudioDrivenRenderValidationReport? audioDrivenValidationReport = null;
            bool renderVisualSelectionReady;
            var failFastErrors = new List<string>();
            if (request.UseAudioDrivenTimeline)
            {
                if (request.MergeAudio) logger.LogInformation("WEEKLY_FINAL_AUDIO_DRIVEN_INPUT_VALIDATION_START pipelineRunId={PipelineRunId}", pipelineRunId);
                audioDrivenValidationReport = BuildAudioDrivenRenderValidationReport(pipelineRunId, paths, loaded, request, longformAudioFound, shortformAudioFound, warnings);
                await File.WriteAllTextAsync(audioDrivenRenderValidationReportPath, JsonSerializer.Serialize(audioDrivenValidationReport, JsonOptions), cancellationToken);
                if (request.MergeAudio) logger.LogInformation("WEEKLY_FINAL_AUDIO_DRIVEN_INPUT_VALIDATION_COMPLETE pipelineRunId={PipelineRunId} audioDrivenInputReady={AudioDrivenInputReady} errorCount={ErrorCount}", pipelineRunId, audioDrivenValidationReport.Errors.Count == 0, audioDrivenValidationReport.Errors.Count);
                visualSelectionReport = EmptyVisualSelectionReport();
                diversityValidationReport = EmptyDiversityValidationReport();
                renderVisualSelectionReady = true;
                if (!hydration.RenderInputHydrationPassed) failFastErrors.Add("renderInputHydrationPassed is false; render input manifest does not include enough production assets.");
                failFastErrors.AddRange(audioDrivenValidationReport.Errors);
            }
            else
            {
                var resolvedShotPlan = BuildResolvedShotPlan(pipelineRunId, commandPlans);
                await File.WriteAllTextAsync(resolvedShotPlanPath, JsonSerializer.Serialize(resolvedShotPlan, JsonOptions), cancellationToken);
                visualSelectionReport = BuildVisualSelectionReport(commandPlans, warnings, errors);
                await File.WriteAllTextAsync(visualSelectionReportPath, JsonSerializer.Serialize(visualSelectionReport, JsonOptions), cancellationToken);
                diversityValidationReport = BuildDiversityValidationReport(visualSelectionReport);
                await File.WriteAllTextAsync(diversityValidationReportPath, JsonSerializer.Serialize(diversityValidationReport, JsonOptions), cancellationToken);
                renderVisualSelectionReady = visualSelectionReport.Errors.Count == 0;
                if (!hydration.RenderInputHydrationPassed) failFastErrors.Add("renderInputHydrationPassed is false; render input manifest does not include enough production assets.");
                if (!renderVisualSelectionReady) failFastErrors.Add("renderVisualSelectionReady is false; resolved shot plan failed segment visual rules.");
                if (!diversityValidationReport.RenderVisualDiversityReady) failFastErrors.Add("renderVisualDiversityReady is false; diversity validation failed.");
                if (!visualSelectionReport.VisualDistributionPassed) failFastErrors.Add("visualDistributionPassed is false; NASA/JWST or scene distribution requirements failed.");
            }
            await File.WriteAllTextAsync(storyboardReportPath, JsonSerializer.Serialize(BuildStoryboardReport(pipelineRunId, commandPlans, loaded.AudioPlan), JsonOptions), cancellationToken);
            errors.AddRange(failFastErrors);

            if (!request.DryRun && failFastErrors.Count == 0)
            {
                foreach (var plan in commandPlans)
                {
                    if (request.MergeAudio) logger.LogInformation("WEEKLY_FINAL_VIDEO_ONLY_RENDER_START pipelineRunId={PipelineRunId} episodeType={EpisodeType} videoOnlyPath={VideoOnlyPath}", pipelineRunId, plan.EpisodeType, plan.OutputPath);
                    var report = await ExecutePlanAsync(plan, request, cancellationToken);
                    if (request.MergeAudio) logger.LogInformation("WEEKLY_FINAL_VIDEO_ONLY_RENDER_COMPLETE pipelineRunId={PipelineRunId} episodeType={EpisodeType} videoOnlyPath={VideoOnlyPath} rendered={Rendered}", pipelineRunId, plan.EpisodeType, plan.OutputPath, report.Result.RenderReport.Rendered);
                    commandReports.Add(report.Report);
                    if (report.Result.EpisodeType.Equals("longform", StringComparison.OrdinalIgnoreCase)) longformResult = report.Result.RenderReport;
                    else shortformResult = report.Result.RenderReport;
                    if (report.Report.Errors.Count > 0)
                    {
                        errors.AddRange(report.Report.Errors.Select(e => $"{plan.EpisodeType}: {e}"));
                    }
                }
            }
            else if (request.DryRun)
            {
                commandReports.AddRange(commandPlans.Select(plan => new WeeklyExistingRunFfmpegCommandReport(plan.EpisodeType, plan.OutputPath, true, false, false, null, 0, plan.Command, null, [], [])));
                logger.LogInformation("WEEKLY_RENDER_DRY_RUN_COMMANDS_CREATED pipelineRunId={PipelineRunId} commandCount={CommandCount}", pipelineRunId, commandPlans.Count);
            }

            WeeklyFinalAudioVideoMergeEpisodeReport longformMerge = NotRequestedMergeReport(videoOnlyLongformOutput, longformAudioPath, finalLongformOutput);
            WeeklyFinalAudioVideoMergeEpisodeReport shortformMerge = NotRequestedMergeReport(videoOnlyShortformOutput, shortformAudioPath, finalShortformOutput);
            var finalAudioVideoMergeReportPath = Path.Combine(renderDirectory, "final-audio-video-merge-report.json");
            var finalRenderReportPath = Path.Combine(renderDirectory, "final-render-report.json");
            if (request.MergeAudio)
            {
                if (request.RenderLongform)
                {
                    longformMerge = skipLongformFinal
                        ? await ExistingFinalAudioVideoMergeReportAsync("longform", videoOnlyLongformOutput, longformAudioPath, finalLongformOutput, errors, cancellationToken)
                        : await MergeFinalAudioVideoAsync(pipelineRunId, "longform", videoOnlyLongformOutput, longformAudioPath, finalLongformOutput, request, warnings, errors, cancellationToken);
                    longformResult = longformResult with { OutputPath = finalLongformOutput, Rendered = !skipLongformFinal && longformMerge.Merged, Skipped = skipLongformFinal, AudioAttached = longformMerge.AudioAttached, DurationSeconds = longformMerge.VideoDurationSeconds };
                }
                if (request.RenderShortform)
                {
                    shortformMerge = skipShortformFinal
                        ? await ExistingFinalAudioVideoMergeReportAsync("shortform", videoOnlyShortformOutput, shortformAudioPath, finalShortformOutput, errors, cancellationToken)
                        : await MergeFinalAudioVideoAsync(pipelineRunId, "shortform", videoOnlyShortformOutput, shortformAudioPath, finalShortformOutput, request, warnings, errors, cancellationToken);
                    shortformResult = shortformResult with { OutputPath = finalShortformOutput, Rendered = !skipShortformFinal && shortformMerge.Merged, Skipped = skipShortformFinal, AudioAttached = shortformMerge.AudioAttached, DurationSeconds = shortformMerge.VideoDurationSeconds };
                }
            }

            var completed = DateTime.UtcNow;
            var failedReport = commandReports.FirstOrDefault(r => r.Errors.Count > 0);
            var longformPlan = commandPlans.FirstOrDefault(p => p.EpisodeType.Equals("longform", StringComparison.OrdinalIgnoreCase));
            var shortformPlan = commandPlans.FirstOrDefault(p => p.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase));
            var useStagedRendering = commandPlans.Any(p => p.UseStagedRendering);
            var renderMode = useStagedRendering ? "staged" : "singleGraph";
            var transitionMode = useStagedRendering ? "simplified" : "full";
            var ffmpegCommandLength = commandPlans.Select(p => p.CommandLength).DefaultIfEmpty(0).Max();
            var ffmpegStderrPath = failedReport is null ? string.Empty : (commandPlans.FirstOrDefault(p => p.EpisodeType.Equals(failedReport.EpisodeType, StringComparison.OrdinalIgnoreCase))?.StderrPath ?? string.Empty);
            var failedStage = BuildFailedStage(failedReport);
            var failedShotNumber = ExtractFailedShotNumber(failedReport);
            var pendingQualityReport = BuildQualityReport(pipelineRunId, commandPlans, warnings);
            var videoReport = new WeeklyExistingRunVideoRenderReport(pipelineRunId, started, completed, request.DryRun, longformResult, shortformResult, renderMode, useStagedRendering, request.DebugStoryboard, longformPlan?.SegmentFiles.Count ?? 0, shortformPlan?.SegmentFiles.Count ?? 0, CountRenderedClips(longformPlan), CountRenderedClips(shortformPlan), transitionMode, ffmpegCommandLength, ffmpegStderrPath, failedStage, failedShotNumber, longformPlan?.RenderMode ?? "notRequested", shortformPlan?.RenderMode ?? "notRequested", pendingQualityReport.VideoOnlyRenderReady, pendingQualityReport.LongformVideoOnlyQualityReady, pendingQualityReport.ShortformVideoOnlyQualityReady, pendingQualityReport.LongformRepetitionPolishPassed, pendingQualityReport.ShortformVerticalLayoutPassed, pendingQualityReport.ShortformSafeAreaPassed, pendingQualityReport.ShortformContainLayoutCount, pendingQualityReport.ShortformSmartCropLayoutCount, pendingQualityReport.ShortformCroppedTextRiskCount, pendingQualityReport.LongformBackToBackRepeatCount, pendingQualityReport.LongformSameAssetSameSegmentRepeatCount, pendingQualityReport.LongformSameFamilyConsecutiveMax, warnings, errors);
            var ffmpegReport = new WeeklyExistingRunFfmpegExecutionReport(pipelineRunId, started, completed, request.DryRun, commandReports, warnings, errors);
            await File.WriteAllTextAsync(videoReportPath, JsonSerializer.Serialize(videoReport, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(ffmpegReportPath, JsonSerializer.Serialize(ffmpegReport, JsonOptions), cancellationToken);
            var qualityReport = pendingQualityReport;
            await File.WriteAllTextAsync(qualityReportPath, JsonSerializer.Serialize(qualityReport, JsonOptions), cancellationToken);
            if (request.UseAudioDrivenTimeline && request.MergeAudio && audioDrivenValidationReport is not null)
            {
                audioDrivenValidationReport = CompleteAudioDrivenRenderValidationReport(request, audioDrivenValidationReport, videoOnlyLongformOutput, videoOnlyShortformOutput, longformMerge, shortformMerge, warnings);
                await File.WriteAllTextAsync(audioDrivenRenderValidationReportPath, JsonSerializer.Serialize(audioDrivenValidationReport, JsonOptions), cancellationToken);
            }
            var audioVideoMergeReady = request.MergeAudio && errors.Count == 0 && (!request.RenderLongform || longformMerge.Merged || request.DryRun) && (!request.RenderShortform || shortformMerge.Merged || request.DryRun);
            var finalVideoRenderReady = request.MergeAudio ? audioVideoMergeReady : false;
            if (request.MergeAudio)
            {
                var mergeReport = new WeeklyFinalAudioVideoMergeReport(pipelineRunId, audioVideoMergeReady, longformMerge, shortformMerge, warnings, errors);
                await File.WriteAllTextAsync(finalAudioVideoMergeReportPath, JsonSerializer.Serialize(mergeReport, JsonOptions), cancellationToken);
                var finalReport = new WeeklyFinalRenderReport(pipelineRunId, started, completed, finalVideoRenderReady, audioVideoMergeReady, longformMerge, shortformMerge, warnings, errors);
                await File.WriteAllTextAsync(finalRenderReportPath, JsonSerializer.Serialize(finalReport, JsonOptions), cancellationToken);
            }

            if (!request.DryRun && failFastErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", failFastErrors));

            var audioFound = request.MergeAudio ? (longformMerge.AudioAttached || shortformMerge.AudioAttached || request.DryRun) : commandPlans.Any(p => p.AudioPath is not null);
            var audioAttached = request.MergeAudio ? (longformMerge.AudioAttached || shortformMerge.AudioAttached) : commandPlans.Any(p => p.AudioAttached);
            var renderedSilent = request.MergeAudio ? false : commandPlans.Count > 0 && commandPlans.Any(p => !p.AudioAttached);
            logger.LogInformation("WEEKLY_RENDER_EXISTING_RUN_COMPLETE pipelineRunId={PipelineRunId} dryRun={DryRun}", pipelineRunId, request.DryRun);
            if (request.MergeAudio) logger.LogInformation("WEEKLY_FINAL_RENDER_COMPLETE pipelineRunId={PipelineRunId} dryRun={DryRun} audioVideoMergeReady={AudioVideoMergeReady}", pipelineRunId, request.DryRun, audioVideoMergeReady);
            return new WeeklyExistingRunRenderResponse(
                pipelineRunId,
                request.MergeAudio ? finalVideoRenderReady : errors.Count == 0 && (request.DryRun || longformResult.Rendered || longformResult.Skipped || shortformResult.Rendered || shortformResult.Skipped),
                request.DryRun,
                request.RenderLongform,
                longformResult.Rendered,
                longformResult.Skipped,
                request.MergeAudio ? finalLongformOutput : longformOutput,
                request.RenderShortform,
                shortformResult.Rendered,
                shortformResult.Skipped,
                request.MergeAudio ? finalShortformOutput : shortformOutput,
                videoReportPath,
                ffmpegReportPath,
                qualityReportPath,
                renderVisualSelectionReady,
                diversityValidationReport.RenderVisualDiversityReady,
                resolvedShotPlanPath,
                visualSelectionReportPath,
                diversityValidationReportPath,
                hydration.RenderInputHydrationPassed,
                hydration.TotalProductionAssetsDiscovered,
                hydration.TotalRenderInputAssets,
                storyboardReportPath,
                root,
                visualSelectionReport.HeroEventWesternGroupingFrameCount,
                visualSelectionReport.PlanetHighlightsWesternGroupingFrameCount,
                visualSelectionReport.ShortformWesternGroupingFrameCount,
                visualSelectionReport.MoonOnlyStellariumDetected,
                visualSelectionReport.MaxLongformShotDurationSeconds,
                visualSelectionReport.MaxShortformShotDurationSeconds,
                visualSelectionReport.RepeatedAssetPathCount,
                visualSelectionReport.AssetRepeatValidationMode,
                diversityValidationReport.AssetRepeatWeightedValidationPassed,
                diversityValidationReport.AssetFamilyDistributionPassed,
                visualSelectionReport.SameAssetPathHardFailureCount,
                visualSelectionReport.SameAssetPathWarningCount,
                visualSelectionReport.SameAssetPathMaxUsageCount,
                visualSelectionReport.SameAssetPathMaxDurationPercent,
                visualSelectionReport.MaxConsecutiveSameAssetPathCount,
                visualSelectionReport.AiCinematicDiversityPassed,
                visualSelectionReport.MotionGraphicDiversityPassed,
                visualSelectionReport.StellariumSceneBalancePassed,
                visualSelectionReport.ShortformPacingPassed,
                visualSelectionReport.LongformPacingPassed,
                visualSelectionReport.VisualDistributionPassed,
                visualSelectionReport.NasaAssetUsageCount,
                visualSelectionReport.JwstAssetUsageCount,
                visualSelectionReport.MotionGraphicUsageCount,
                visualSelectionReport.AiCinematicDistinctUsageCount,
                visualSelectionReport.MoonHeroVisualDurationPercent,
                !request.AllowSilent,
                audioFound,
                audioAttached,
                renderedSilent,
                request.DebugStoryboard && !request.DryRun && (longformResult.Rendered || shortformResult.Rendered),
                Path.Combine(renderDirectory, "longform", "weekly-skyforecast-longform-debug.mp4"),
                Path.Combine(renderDirectory, "shortform", "weekly-skyforecast-shortform-debug.mp4"),
                useStagedRendering,
                renderMode,
                longformPlan?.SegmentFiles.Count ?? 0,
                CountRenderedClips(longformPlan),
                shortformPlan?.SegmentFiles.Count ?? 0,
                CountRenderedClips(shortformPlan),
                transitionMode,
                qualityReport.VideoOnlyRenderReady,
                qualityReport.LongformVideoOnlyQualityReady,
                qualityReport.ShortformVideoOnlyQualityReady,
                qualityReport.LongformRepetitionPolishPassed,
                qualityReport.ShortformVerticalLayoutPassed,
                qualityReport.ShortformSafeAreaPassed,
                qualityReport.LongformRenderMode,
                qualityReport.ShortformRenderMode,
                qualityReport.ShortformContainLayoutCount,
                qualityReport.ShortformSmartCropLayoutCount,
                qualityReport.ShortformCroppedTextRiskCount,
                qualityReport.LongformBackToBackRepeatCount,
                qualityReport.LongformSameAssetSameSegmentRepeatCount,
                qualityReport.LongformSameFamilyConsecutiveMax,
                failedReport?.ExitCode,
                ffmpegStderrPath,
                failedReport is null ? string.Empty : commandPlanPath,
                failedStage ?? string.Empty,
                failedShotNumber,
                commandPlans,
                finalVideoRenderReady,
                audioVideoMergeReady,
                request.RenderLongform && (longformMerge.Merged || request.DryRun),
                request.RenderShortform && (shortformMerge.Merged || request.DryRun),
                finalLongformOutput,
                finalShortformOutput,
                longformMerge.AudioAttached,
                shortformMerge.AudioAttached,
                longformMerge.VideoDurationSeconds,
                shortformMerge.VideoDurationSeconds,
                longformMerge.DurationDeltaSeconds,
                shortformMerge.DurationDeltaSeconds,
                finalAudioVideoMergeReportPath,
                finalRenderReportPath,
                request.UseAudioDrivenTimeline,
                audioDrivenValidationReport?.AudioDrivenRenderReady ?? false,
                request.UseAudioDrivenTimeline ? audioDrivenRenderValidationReportPath : string.Empty,
                audioDrivenValidationReport?.AudioDrivenTimelineLoaded ?? false,
                audioDrivenValidationReport?.AudioDrivenShotPlanLoaded ?? false,
                audioDrivenValidationReport?.AudioDrivenRenderContractLoaded ?? false,
                longformAudioFound,
                shortformAudioFound,
                request.RenderLongform && File.Exists(videoOnlyLongformOutput),
                request.RenderShortform && File.Exists(videoOnlyShortformOutput),
                request.RenderLongform && (request.DryRun || File.Exists(videoOnlyLongformOutput)),
                request.RenderShortform && (request.DryRun || File.Exists(videoOnlyShortformOutput)),
                audioDrivenValidationReport?.LongformAudioDurationSeconds ?? longformMerge.AudioDurationSeconds,
                audioDrivenValidationReport?.ShortformAudioDurationSeconds ?? shortformMerge.AudioDurationSeconds,
                audioDrivenValidationReport?.LongformVideoDurationSeconds ?? longformMerge.VideoDurationSeconds,
                audioDrivenValidationReport?.ShortformVideoDurationSeconds ?? shortformMerge.VideoDurationSeconds,
                audioDrivenValidationReport?.Errors ?? [],
                warnings,
                errors);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            logger.LogError(ex, "WEEKLY_RENDER_EXISTING_RUN_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
            if (request.MergeAudio)
            {
                logger.LogError(ex, "WEEKLY_FINAL_RENDER_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
                await TryWriteFailedFinalRenderReportsAsync(pipelineRunId, request, started, warnings, errors, cancellationToken);
            }
            throw;
        }
    }



    private async Task TryWriteFailedFinalRenderReportsAsync(Guid pipelineRunId, WeeklyExistingRunRenderRequest request, DateTime started, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, CancellationToken cancellationToken)
    {
        try
        {
            var root = await pipelineRunDirectoryResolver.ResolveRunDirectoryAsync(pipelineRunId);
            var renderDirectory = Path.Combine(root, "render");
            Directory.CreateDirectory(renderDirectory);
            var longformAudioPath = Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3");
            var shortformAudioPath = Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3");
            var videoOnlyLongformOutput = Path.Combine(renderDirectory, "temp", "final", "longform", request.DebugStoryboard ? "weekly-skyforecast-longform-video-only-debug.mp4" : "weekly-skyforecast-longform-video-only.mp4");
            var videoOnlyShortformOutput = Path.Combine(renderDirectory, "temp", "final", "shortform", request.DebugStoryboard ? "weekly-skyforecast-shortform-video-only-debug.mp4" : "weekly-skyforecast-shortform-video-only.mp4");
            var finalLongformOutput = Path.Combine(renderDirectory, "longform", request.DebugStoryboard ? "weekly-skyforecast-longform-final-debug.mp4" : "weekly-skyforecast-longform-final.mp4");
            var finalShortformOutput = Path.Combine(renderDirectory, "shortform", request.DebugStoryboard ? "weekly-skyforecast-shortform-final-debug.mp4" : "weekly-skyforecast-shortform-final.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(videoOnlyLongformOutput) ?? renderDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(videoOnlyShortformOutput) ?? renderDirectory);

            if (request.UseAudioDrivenTimeline)
            {
                var report = new WeeklyAudioDrivenRenderValidationReport(
                    pipelineRunId,
                    false,
                    File.Exists(Path.Combine(renderDirectory, "audio-driven-final-render-timeline.json")),
                    File.Exists(Path.Combine(renderDirectory, "audio-driven-resolved-render-shot-plan.json")),
                    File.Exists(Path.Combine(renderDirectory, "audio-driven-render-contract.json")),
                    File.Exists(longformAudioPath),
                    File.Exists(shortformAudioPath),
                    File.Exists(videoOnlyLongformOutput),
                    File.Exists(videoOnlyShortformOutput),
                    request.RenderLongform && File.Exists(videoOnlyLongformOutput),
                    request.RenderShortform && File.Exists(videoOnlyShortformOutput),
                    0, 0, 0, 0, 0, 0,
                    false,
                    false,
                    false,
                    0,
                    0,
                    errors.ToList(),
                    warnings.ToList(),
                    errors.ToList());
                await File.WriteAllTextAsync(Path.Combine(renderDirectory, "audio-driven-render-validation-report.json"), JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
            }

            var longformMerge = NotRequestedMergeReport(videoOnlyLongformOutput, longformAudioPath, finalLongformOutput);
            var shortformMerge = NotRequestedMergeReport(videoOnlyShortformOutput, shortformAudioPath, finalShortformOutput);
            var mergeReport = new WeeklyFinalAudioVideoMergeReport(pipelineRunId, false, longformMerge, shortformMerge, warnings, errors);
            await File.WriteAllTextAsync(Path.Combine(renderDirectory, "final-audio-video-merge-report.json"), JsonSerializer.Serialize(mergeReport, JsonOptions), cancellationToken);
            var finalReport = new WeeklyFinalRenderReport(pipelineRunId, started, DateTime.UtcNow, false, false, longformMerge, shortformMerge, warnings, errors);
            await File.WriteAllTextAsync(Path.Combine(renderDirectory, "final-render-report.json"), JsonSerializer.Serialize(finalReport, JsonOptions), cancellationToken);
        }
        catch (Exception reportEx)
        {
            logger.LogWarning(reportEx, "WEEKLY_FINAL_RENDER_FAILURE_REPORT_WRITE_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        }
    }

    private static WeeklyAudioDrivenRenderValidationReport BuildAudioDrivenRenderValidationReport(Guid pipelineRunId, WeeklyExistingRunRequiredPaths paths, WeeklyExistingRunLoadedInputs loaded, WeeklyExistingRunRenderRequest request, bool longformAudioFound, bool shortformAudioFound, IReadOnlyList<string> warnings)
    {
        var errors = new List<string>();
        var audioDrivenTimelineLoaded = File.Exists(paths.FinalTimeline) && loaded.Timeline.PipelineRunId == pipelineRunId;
        var audioDrivenShotPlanLoaded = File.Exists(paths.ResolvedRenderShotPlan) && loaded.ResolvedShotPlan is not null && loaded.ResolvedShotPlan.PipelineRunId == pipelineRunId;
        var audioDrivenRenderContractLoaded = File.Exists(paths.RenderContract) && loaded.Contract.PipelineRunId == pipelineRunId;
        if (!audioDrivenTimelineLoaded) errors.Add($"Audio-driven final render timeline is missing or invalid: {paths.FinalTimeline}");
        if (!audioDrivenShotPlanLoaded) errors.Add($"Audio-driven resolved render shot plan is missing or invalid: {paths.ResolvedRenderShotPlan}");
        if (!audioDrivenRenderContractLoaded) errors.Add($"Audio-driven render contract is missing or invalid: {paths.RenderContract}");
        if (request.RenderLongform && !longformAudioFound) errors.Add("Longform audio file was not found for audio-driven final render.");
        if (request.RenderShortform && !shortformAudioFound) errors.Add("Shortform audio file was not found for audio-driven final render.");

        var timelineIssues = ValidateAudioDrivenTimelineShape(loaded.Timeline);
        var shotPlanIssues = loaded.ResolvedShotPlan is null
            ? (ShotDurationsValid: false, NoGaps: false, NoOverlaps: false, Errors: (IReadOnlyList<string>)new[] { "Audio-driven shot plan could not be loaded." })
            : ValidateAudioDrivenShotPlanShape(loaded.ResolvedShotPlan);
        errors.AddRange(timelineIssues.Errors);
        errors.AddRange(shotPlanIssues.Errors);
        var shotDurationsValid = timelineIssues.ShotDurationsValid && shotPlanIssues.ShotDurationsValid;
        var noGaps = timelineIssues.NoGaps && shotPlanIssues.NoGaps;
        var noOverlaps = timelineIssues.NoOverlaps && shotPlanIssues.NoOverlaps;
        var audioFilesFound = (!request.RenderLongform || longformAudioFound) && (!request.RenderShortform || shortformAudioFound);
        var ready = audioDrivenTimelineLoaded && audioDrivenShotPlanLoaded && audioDrivenRenderContractLoaded && audioFilesFound && shotDurationsValid && noGaps && noOverlaps && errors.Count == 0;

        return new WeeklyAudioDrivenRenderValidationReport(
            pipelineRunId,
            ready,
            audioDrivenTimelineLoaded,
            audioDrivenShotPlanLoaded,
            audioDrivenRenderContractLoaded,
            longformAudioFound,
            shortformAudioFound,
            false,
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            shotDurationsValid,
            noGaps,
            noOverlaps,
            Round(loaded.Timeline.Longform.ActualDurationSeconds),
            Round(loaded.Timeline.Shortform.ActualDurationSeconds),
            errors.ToList(),
            warnings.ToList(),
            errors);
    }

    private static WeeklyAudioDrivenRenderValidationReport CompleteAudioDrivenRenderValidationReport(WeeklyExistingRunRenderRequest request, WeeklyAudioDrivenRenderValidationReport inputReport, string longformVideoOnlyPath, string shortformVideoOnlyPath, WeeklyFinalAudioVideoMergeEpisodeReport longformMerge, WeeklyFinalAudioVideoMergeEpisodeReport shortformMerge, IReadOnlyList<string> warnings)
    {
        var validationErrors = inputReport.Errors.ToList();
        var longformVideoOnlyExists = File.Exists(longformVideoOnlyPath);
        var shortformVideoOnlyExists = File.Exists(shortformVideoOnlyPath);
        var longformVideoOnlyRendered = request.RenderLongform && (request.DryRun || longformVideoOnlyExists);
        var shortformVideoOnlyRendered = request.RenderShortform && (request.DryRun || shortformVideoOnlyExists);

        ValidateAudioDrivenEpisodeDurations("longform", request.RenderLongform, request.DryRun, longformVideoOnlyExists, longformMerge, validationErrors);
        ValidateAudioDrivenEpisodeDurations("shortform", request.RenderShortform, request.DryRun, shortformVideoOnlyExists, shortformMerge, validationErrors);

        var ready = inputReport.AudioDrivenTimelineLoaded
            && inputReport.AudioDrivenShotPlanLoaded
            && inputReport.AudioDrivenRenderContractLoaded
            && (!request.RenderLongform || inputReport.LongformAudioFound)
            && (!request.RenderShortform || inputReport.ShortformAudioFound)
            && inputReport.AudioDrivenShotDurationsValid
            && inputReport.AudioDrivenNoGaps
            && inputReport.AudioDrivenNoOverlaps
            && (!request.RenderLongform || request.DryRun || longformMerge.Merged)
            && (!request.RenderShortform || request.DryRun || shortformMerge.Merged)
            && validationErrors.Count == 0;

        return inputReport with
        {
            AudioDrivenRenderReady = ready,
            LongformVideoOnlyExists = longformVideoOnlyExists,
            ShortformVideoOnlyExists = shortformVideoOnlyExists,
            LongformVideoOnlyRendered = longformVideoOnlyRendered,
            ShortformVideoOnlyRendered = shortformVideoOnlyRendered,
            LongformAudioDurationSeconds = longformMerge.AudioDurationSeconds,
            ShortformAudioDurationSeconds = shortformMerge.AudioDurationSeconds,
            LongformVideoDurationSeconds = longformMerge.VideoDurationSeconds,
            ShortformVideoDurationSeconds = shortformMerge.VideoDurationSeconds,
            LongformDurationDeltaSeconds = longformMerge.DurationDeltaSeconds,
            ShortformDurationDeltaSeconds = shortformMerge.DurationDeltaSeconds,
            AudioDrivenValidationErrors = validationErrors,
            Warnings = warnings.ToList(),
            Errors = validationErrors
        };
    }

    private static void ValidateAudioDrivenEpisodeDurations(string episodeType, bool requested, bool dryRun, bool videoOnlyExists, WeeklyFinalAudioVideoMergeEpisodeReport merge, List<string> validationErrors)
    {
        if (!requested || dryRun) return;
        if (!videoOnlyExists && !merge.Merged) validationErrors.Add($"{episodeType} video-only output does not exist after render: {merge.VideoOnlyPath}");
        if (merge.AudioDurationSeconds <= 0) validationErrors.Add($"{episodeType} audio duration could not be probed: {merge.AudioPath}");
        if (merge.VideoDurationSeconds <= 0) validationErrors.Add($"{episodeType} video-only duration could not be probed: {merge.VideoOnlyPath}");
        if (merge.DurationDeltaSeconds > FinalAudioVideoDurationToleranceSeconds) validationErrors.Add($"{episodeType} video/audio duration delta is {merge.DurationDeltaSeconds:0.###}s, exceeding allowed tolerance of {FinalAudioVideoDurationToleranceSeconds:0.###}s.");
        if (!merge.Merged) validationErrors.Add($"{episodeType} final audio/video merge did not complete successfully: {merge.FinalVideoPath}");
    }

    private static (bool ShotDurationsValid, bool NoGaps, bool NoOverlaps, IReadOnlyList<string> Errors) ValidateAudioDrivenTimelineShape(FinalRenderTimeline timeline)
    {
        var errors = new List<string>();
        var longform = ValidateTimelineEpisodeShape("longform", timeline.Longform);
        var shortform = ValidateTimelineEpisodeShape("shortform", timeline.Shortform);
        errors.AddRange(longform.Errors);
        errors.AddRange(shortform.Errors);
        return (longform.ShotDurationsValid && shortform.ShotDurationsValid, longform.NoGaps && shortform.NoGaps, longform.NoOverlaps && shortform.NoOverlaps, errors);
    }

    private static (bool ShotDurationsValid, bool NoGaps, bool NoOverlaps, IReadOnlyList<string> Errors) ValidateTimelineEpisodeShape(string episodeType, FinalRenderEpisodeTimeline episode)
    {
        var errors = new List<string>();
        var shotDurationsValid = true;
        var noGaps = true;
        var noOverlaps = true;
        ValidateIntervals($"{episodeType} segment", episode.Segments.Select(s => (s.StartSecond, s.EndSecond, s.DurationSeconds)), errors, ref shotDurationsValid, ref noGaps, ref noOverlaps);
        foreach (var segment in episode.Segments)
        {
            ValidateIntervals($"{episodeType} segment {segment.SegmentId} shot", segment.Shots.Select(s => (s.StartSecond, s.EndSecond, s.DurationSeconds)), errors, ref shotDurationsValid, ref noGaps, ref noOverlaps);
        }
        return (shotDurationsValid, noGaps, noOverlaps, errors);
    }

    private static (bool ShotDurationsValid, bool NoGaps, bool NoOverlaps, IReadOnlyList<string> Errors) ValidateAudioDrivenShotPlanShape(ResolvedRenderShotPlan shotPlan)
    {
        var errors = new List<string>();
        var shotDurationsValid = true;
        var noGaps = true;
        var noOverlaps = true;
        foreach (var episode in shotPlan.Episodes)
        {
            ValidateIntervals($"{episode.EpisodeType} resolved segment", episode.Segments.Select(s => (s.StartSecond, s.EndSecond, s.DurationSeconds)), errors, ref shotDurationsValid, ref noGaps, ref noOverlaps);
            foreach (var segment in episode.Segments)
            {
                ValidateIntervals($"{episode.EpisodeType} resolved segment {segment.SegmentId} shot", segment.Shots.Select(s => (s.StartSecond, s.EndSecond, s.DurationSeconds)), errors, ref shotDurationsValid, ref noGaps, ref noOverlaps);
            }
        }
        return (shotDurationsValid, noGaps, noOverlaps, errors);
    }

    private static void ValidateIntervals(string label, IEnumerable<(double StartSecond, double EndSecond, double DurationSeconds)> intervals, List<string> errors, ref bool durationsValid, ref bool noGaps, ref bool noOverlaps)
    {
        const double tolerance = 0.001d;
        var ordered = intervals.OrderBy(i => i.StartSecond).ToList();
        double? previousEnd = null;
        foreach (var interval in ordered)
        {
            if (interval.DurationSeconds <= 0 || interval.EndSecond <= interval.StartSecond || Math.Abs((interval.EndSecond - interval.StartSecond) - interval.DurationSeconds) > tolerance)
            {
                durationsValid = false;
                errors.Add($"{label} duration is invalid: start={interval.StartSecond:0.###} end={interval.EndSecond:0.###} duration={interval.DurationSeconds:0.###}.");
            }
            if (previousEnd is not null)
            {
                var delta = interval.StartSecond - previousEnd.Value;
                if (delta > tolerance)
                {
                    noGaps = false;
                    errors.Add($"{label} has a gap of {delta:0.###}s before start={interval.StartSecond:0.###}.");
                }
                if (delta < -tolerance)
                {
                    noOverlaps = false;
                    errors.Add($"{label} overlaps by {Math.Abs(delta):0.###}s at start={interval.StartSecond:0.###}.");
                }
            }
            previousEnd = interval.EndSecond;
        }
    }

    private static RenderVisualSelectionReport EmptyVisualSelectionReport()
        => new(0, 0, 0, 0, 0, false, 0, 0, false, 0, "skippedForAudioDrivenTimeline", 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, double>(), true, true, true, 0, 0, true, true, true, true, true, true, 0, 0, 0, 0, 0, new Dictionary<string, int>(), new Dictionary<string, int>(), [], []);

    private static RenderDiversityValidationReport EmptyDiversityValidationReport()
        => new(true, true, true, true, true, true, "skippedForAudioDrivenTimeline", true, true, 0, 0, 0, true, true, true, true, [], []);

    private async Task<(bool SkipLongformFinal, bool SkipShortformFinal)> PrepareFinalRenderOutputsAsync(Guid pipelineRunId, WeeklyExistingRunRenderRequest request, string videoOnlyLongformOutput, string videoOnlyShortformOutput, string finalLongformOutput, string finalShortformOutput, CancellationToken cancellationToken)
    {
        var skipLongform = request.RenderLongform && !request.OverwriteExisting && await IsExistingFinalOutputValidAsync(finalLongformOutput, cancellationToken);
        var skipShortform = request.RenderShortform && !request.OverwriteExisting && await IsExistingFinalOutputValidAsync(finalShortformOutput, cancellationToken);

        if (request.RenderLongform && !skipLongform)
        {
            DeleteIfExists(videoOnlyLongformOutput);
            if (request.OverwriteExisting) DeleteIfExists(finalLongformOutput);
        }
        if (request.RenderShortform && !skipShortform)
        {
            DeleteIfExists(videoOnlyShortformOutput);
            if (request.OverwriteExisting) DeleteIfExists(finalShortformOutput);
        }

        logger.LogInformation("WEEKLY_FINAL_STALE_FILE_PROTECTION_COMPLETE pipelineRunId={PipelineRunId} overwriteExisting={OverwriteExisting} skipLongformFinal={SkipLongformFinal} skipShortformFinal={SkipShortformFinal}", pipelineRunId, request.OverwriteExisting, skipLongform, skipShortform);
        return (skipLongform, skipShortform);
    }

    private async Task<bool> IsExistingFinalOutputValidAsync(string finalVideoPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(finalVideoPath) || new FileInfo(finalVideoPath).Length <= 0) return false;
        var info = await ProbeMediaAsync(finalVideoPath, cancellationToken);
        return info.HasVideo && info.HasAudio && info.DurationSeconds > 0;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private async Task<WeeklyFinalAudioVideoMergeEpisodeReport> ExistingFinalAudioVideoMergeReportAsync(string episodeType, string videoOnlyPath, string audioPath, string finalVideoPath, List<string> errors, CancellationToken cancellationToken)
    {
        var finalInfo = await ProbeMediaAsync(finalVideoPath, cancellationToken);
        var audioInfo = await ProbeMediaAsync(audioPath, cancellationToken);
        var delta = audioInfo.DurationSeconds > 0 ? Math.Round(Math.Abs(finalInfo.DurationSeconds - audioInfo.DurationSeconds), 3) : 0;
        if (!finalInfo.HasVideo) errors.Add($"{episodeType} existing final output has no video stream: {finalVideoPath}");
        if (!finalInfo.HasAudio) errors.Add($"{episodeType} existing final output has no audio stream: {finalVideoPath}");
        return new WeeklyFinalAudioVideoMergeEpisodeReport(true, videoOnlyPath, audioPath, finalVideoPath, Round(finalInfo.DurationSeconds), Round(audioInfo.DurationSeconds), delta, finalInfo.HasAudio, finalInfo.HasAudio, finalInfo.HasVideo, finalInfo.HasVideo && finalInfo.HasAudio);
    }

    private async Task<WeeklyFinalAudioVideoMergeEpisodeReport> MergeFinalAudioVideoAsync(Guid pipelineRunId, string episodeType, string videoOnlyPath, string audioPath, string finalVideoPath, WeeklyExistingRunRenderRequest request, List<string> warnings, List<string> errors, CancellationToken cancellationToken)
    {
        var requested = episodeType.Equals("longform", StringComparison.OrdinalIgnoreCase) ? request.RenderLongform : request.RenderShortform;
        if (!requested) return NotRequestedMergeReport(videoOnlyPath, audioPath, finalVideoPath);

        Directory.CreateDirectory(Path.GetDirectoryName(finalVideoPath) ?? ".");
        if (request.DryRun)
        {
            return new WeeklyFinalAudioVideoMergeEpisodeReport(true, videoOnlyPath, audioPath, finalVideoPath, 0, 0, 0, false, false, false, false);
        }

        logger.LogInformation("WEEKLY_FINAL_DURATION_COMPARE_START pipelineRunId={PipelineRunId} episodeType={EpisodeType} videoOnlyPath={VideoOnlyPath} audioPath={AudioPath}", pipelineRunId, episodeType, videoOnlyPath, audioPath);
        if (!File.Exists(videoOnlyPath))
        {
            var message = $"{episodeType} video-only render is missing: {videoOnlyPath}";
            errors.Add(message);
            logger.LogInformation("WEEKLY_FINAL_DURATION_COMPARE_COMPLETE pipelineRunId={PipelineRunId} episodeType={EpisodeType} videoDurationSeconds={VideoDurationSeconds} audioDurationSeconds={AudioDurationSeconds} durationDeltaSeconds={DurationDeltaSeconds}", pipelineRunId, episodeType, 0, 0, 0);
            return new WeeklyFinalAudioVideoMergeEpisodeReport(true, videoOnlyPath, audioPath, finalVideoPath, 0, 0, 0, false, false, false, false);
        }
        if (!File.Exists(audioPath))
        {
            var message = $"{episodeType} narration audio is missing: {audioPath}";
            errors.Add(message);
            logger.LogInformation("WEEKLY_FINAL_DURATION_COMPARE_COMPLETE pipelineRunId={PipelineRunId} episodeType={EpisodeType} videoDurationSeconds={VideoDurationSeconds} audioDurationSeconds={AudioDurationSeconds} durationDeltaSeconds={DurationDeltaSeconds}", pipelineRunId, episodeType, 0, 0, 0);
            return new WeeklyFinalAudioVideoMergeEpisodeReport(true, videoOnlyPath, audioPath, finalVideoPath, 0, 0, 0, false, false, false, false);
        }

        var videoInfo = await ProbeMediaAsync(videoOnlyPath, cancellationToken);
        var audioInfo = await ProbeMediaAsync(audioPath, cancellationToken);
        var durationDelta = Math.Round(Math.Abs(videoInfo.DurationSeconds - audioInfo.DurationSeconds), 3);
        logger.LogInformation("WEEKLY_FINAL_DURATION_COMPARE_COMPLETE pipelineRunId={PipelineRunId} episodeType={EpisodeType} videoDurationSeconds={VideoDurationSeconds} audioDurationSeconds={AudioDurationSeconds} durationDeltaSeconds={DurationDeltaSeconds}", pipelineRunId, episodeType, videoInfo.DurationSeconds, audioInfo.DurationSeconds, durationDelta);

        if (!videoInfo.HasVideo)
        {
            errors.Add($"{episodeType} video-only output has no video stream: {videoOnlyPath}");
        }
        if (audioInfo.DurationSeconds <= 0)
        {
            errors.Add($"{episodeType} audio duration could not be probed: {audioPath}");
        }
        if (videoInfo.DurationSeconds <= 0)
        {
            errors.Add($"{episodeType} video duration could not be probed: {videoOnlyPath}");
        }
        if (durationDelta > FinalAudioVideoDurationToleranceSeconds)
        {
            errors.Add($"{episodeType} audio/video duration mismatch is {durationDelta:0.###}s, exceeding allowed tolerance of {FinalAudioVideoDurationToleranceSeconds:0.###}s. Video={videoInfo.DurationSeconds:0.###}s Audio={audioInfo.DurationSeconds:0.###}s.");
            if (request.OverwriteExisting && File.Exists(finalVideoPath)) File.Delete(finalVideoPath);
            return new WeeklyFinalAudioVideoMergeEpisodeReport(true, videoOnlyPath, audioPath, finalVideoPath, Round(videoInfo.DurationSeconds), Round(audioInfo.DurationSeconds), durationDelta, false, false, videoInfo.HasVideo, false);
        }

        if (File.Exists(finalVideoPath) && !request.OverwriteExisting)
        {
            var existing = await ProbeMediaAsync(finalVideoPath, cancellationToken);
            var existingDelta = Math.Round(Math.Abs(existing.DurationSeconds - audioInfo.DurationSeconds), 3);
            return new WeeklyFinalAudioVideoMergeEpisodeReport(true, videoOnlyPath, audioPath, finalVideoPath, Round(videoInfo.DurationSeconds), Round(audioInfo.DurationSeconds), existingDelta, existing.HasAudio, existing.HasAudio, existing.HasVideo, existing.HasAudio && existing.HasVideo && existingDelta <= FinalAudioVideoDurationToleranceSeconds);
        }

        logger.LogInformation("WEEKLY_FINAL_AUDIO_VIDEO_MERGE_START pipelineRunId={PipelineRunId} episodeType={EpisodeType} finalVideoPath={FinalVideoPath}", pipelineRunId, episodeType, finalVideoPath);
        var stderrPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(finalVideoPath)!)!, "logs", $"ffmpeg-final-{episodeType}-merge-stderr.txt");
        var mergeArgs = BuildFinalMergeArguments(videoOnlyPath, audioPath, finalVideoPath).ToList();
        var execution = await RunFfmpegAsync(mergeArgs, stderrPath, cancellationToken);
        if (execution.ExitCode != 0)
        {
            errors.Add($"{episodeType} final audio/video merge FFmpeg exited with code {execution.ExitCode}.");
        }
        if (!File.Exists(finalVideoPath) || new FileInfo(finalVideoPath).Length <= 0)
        {
            errors.Add($"{episodeType} final output was not created or was empty: {finalVideoPath}");
        }

        var finalInfo = await ProbeMediaAsync(finalVideoPath, cancellationToken);
        var finalDelta = Math.Round(Math.Abs(finalInfo.DurationSeconds - audioInfo.DurationSeconds), 3);
        if (!finalInfo.HasVideo) errors.Add($"{episodeType} final output has no video stream: {finalVideoPath}");
        if (!finalInfo.HasAudio) errors.Add($"{episodeType} final output has no audio stream: {finalVideoPath}");
        if (finalDelta > FinalAudioVideoDurationToleranceSeconds) errors.Add($"{episodeType} final duration delta is {finalDelta:0.###}s, exceeding allowed tolerance of {FinalAudioVideoDurationToleranceSeconds:0.###}s.");
        var merged = execution.ExitCode == 0 && File.Exists(finalVideoPath) && finalInfo.HasVideo && finalInfo.HasAudio && finalDelta <= FinalAudioVideoDurationToleranceSeconds;
        logger.LogInformation("WEEKLY_FINAL_AUDIO_VIDEO_MERGE_COMPLETE pipelineRunId={PipelineRunId} episodeType={EpisodeType} merged={Merged} hasVideoStream={HasVideoStream} hasAudioStream={HasAudioStream} finalDurationSeconds={FinalDurationSeconds}", pipelineRunId, episodeType, merged, finalInfo.HasVideo, finalInfo.HasAudio, finalInfo.DurationSeconds);
        return new WeeklyFinalAudioVideoMergeEpisodeReport(true, videoOnlyPath, audioPath, finalVideoPath, Round(videoInfo.DurationSeconds), Round(audioInfo.DurationSeconds), durationDelta, finalInfo.HasAudio, finalInfo.HasAudio, finalInfo.HasVideo, merged);
    }

    private static WeeklyFinalAudioVideoMergeEpisodeReport NotRequestedMergeReport(string videoOnlyPath, string audioPath, string finalVideoPath)
        => new(false, videoOnlyPath, audioPath, finalVideoPath, 0, 0, 0, false, false, false, false);

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static IEnumerable<string> BuildFinalMergeArguments(string videoOnlyPath, string audioPath, string finalVideoPath)
    {
        yield return "-y";
        yield return "-i";
        yield return videoOnlyPath;
        yield return "-i";
        yield return audioPath;
        yield return "-c:v";
        yield return "copy";
        yield return "-c:a";
        yield return "aac";
        yield return "-b:a";
        yield return "192k";
        yield return "-shortest";
        yield return "-movflags";
        yield return "+faststart";
        yield return finalVideoPath;
    }

    private async Task<(double DurationSeconds, bool HasVideo, bool HasAudio)> ProbeMediaAsync(string mediaPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath)) return (0, false, false);
        var ffprobePath = ResolveFfprobePath();
        var processStart = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        processStart.ArgumentList.Add("-v");
        processStart.ArgumentList.Add("quiet");
        processStart.ArgumentList.Add("-print_format");
        processStart.ArgumentList.Add("json");
        processStart.ArgumentList.Add("-show_streams");
        processStart.ArgumentList.Add("-show_format");
        processStart.ArgumentList.Add(mediaPath);
        using var process = Process.Start(processStart) ?? throw new InvalidOperationException("Failed to start FFprobe process.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return (0, false, false);
        using var doc = JsonDocument.Parse(stdout);
        var hasVideo = doc.RootElement.TryGetProperty("streams", out var streams) && streams.EnumerateArray().Any(s => s.TryGetProperty("codec_type", out var t) && string.Equals(t.GetString(), "video", StringComparison.OrdinalIgnoreCase));
        var hasAudio = doc.RootElement.TryGetProperty("streams", out streams) && streams.EnumerateArray().Any(s => s.TryGetProperty("codec_type", out var t) && string.Equals(t.GetString(), "audio", StringComparison.OrdinalIgnoreCase));
        var duration = 0d;
        if (doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var durationElement))
        {
            var durationText = durationElement.GetString();
            double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
        }
        return (Math.Max(0d, duration), hasVideo, hasAudio);
    }

    private string ResolveFfprobePath()
    {
        if (!string.IsNullOrWhiteSpace(_renderingOptions.FfprobePath)) return _renderingOptions.FfprobePath;
        if (!string.IsNullOrWhiteSpace(_renderingOptions.FfmpegPath))
        {
            var ffmpegDirectory = Path.GetDirectoryName(_renderingOptions.FfmpegPath);
            var ffprobeFileName = _renderingOptions.FfmpegPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "ffprobe.exe" : "ffprobe";
            if (!string.IsNullOrWhiteSpace(ffmpegDirectory)) return Path.Combine(ffmpegDirectory, ffprobeFileName);
        }
        return "ffprobe";
    }

    private async Task<WeeklyExistingRunFfmpegCommandPlan> BuildCommandPlanAsync(string episodeType, WeeklyEpisodeRenderContract contract, FinalRenderEpisodeTimeline timeline, WeeklyRenderInputManifest manifest, WeeklyProductionAssetManifest? productionManifest, string expectedAudioPath, string outputPath, WeeklyExistingRunRenderRequest request, List<string> warnings, CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)!)!, "temp", episodeType);
        var clipsDirectory = Path.Combine(tempDirectory, "clips");
        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(clipsDirectory);

        var refinedTimeline = RefineTimelineForRender(episodeType, timeline, manifest, productionManifest, warnings);
        var shots = refinedTimeline.Segments.SelectMany(segment => segment.Shots.Select(shot => (Segment: segment, Shot: shot))).ToList();
        var segmentFiles = new List<string>();
        var index = 0;
        foreach (var (_, shot) in shots)
        {
            index++;
            segmentFiles.Add(Path.Combine(clipsDirectory, $"{index:0000}.mp4"));
        }

        var concatPath = Path.Combine(tempDirectory, "shot-plan.json");
        await File.WriteAllTextAsync(concatPath, JsonSerializer.Serialize(refinedTimeline, JsonOptions), cancellationToken);

        var audioPath = request.MergeAudio ? null : (File.Exists(expectedAudioPath) ? expectedAudioPath : null);
        if (audioPath is null && !request.MergeAudio)
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

        var debugFontPath = ResolveDebugFontPath();
        if (request.DebugStoryboard && string.IsNullOrWhiteSpace(debugFontPath))
        {
            warnings.Add($"{episodeType} debug storyboard drawtext overlay skipped because no usable font path was found; render-storyboard-report.json was still generated.");
        }
        var singleGraphArguments = BuildFfmpegArguments(refinedTimeline, audioPath, audioPath is not null, contract, outputPath, request.DebugStoryboard, debugFontPath).ToList();
        var singleGraphCommand = BuildCommandString(_renderingOptions.FfmpegPath, singleGraphArguments);
        var useStagedRendering = request.UseStagedRendering ?? true;
        if (!useStagedRendering && singleGraphCommand.Length > 25000)
        {
            useStagedRendering = true;
            warnings.Add("Switched to staged rendering because FFmpeg command was too long.");
        }

        if (useStagedRendering)
        {
            warnings.Add($"{episodeType} staged rendering enabled; transitionMode=simplified uses normalized per-shot clips and concat demuxer for stability.");
        }
        var arguments = useStagedRendering
            ? BuildFfmpegArguments(GetConcatInputPath(tempDirectory), audioPath, audioPath is not null, contract, outputPath).ToList()
            : singleGraphArguments;
        var command = BuildCommandString(_renderingOptions.FfmpegPath, arguments);
        var stderrPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)!)!, "logs", episodeType.Equals("longform", StringComparison.OrdinalIgnoreCase) ? "ffmpeg-longform-stderr.txt" : "ffmpeg-shortform-stderr.txt");
        return new WeeklyExistingRunFfmpegCommandPlan(episodeType, outputPath, concatPath, audioPath, audioPath is not null, request.DebugStoryboard, command, segmentFiles, arguments, useStagedRendering, useStagedRendering ? "staged" : "singleGraph", tempDirectory, clipsDirectory, stderrPath, Math.Max(singleGraphCommand.Length, command.Length), qualityMetrics);
    }

    private static FinalRenderEpisodeTimeline RefineTimelineForRender(string episodeType, FinalRenderEpisodeTimeline timeline, WeeklyRenderInputManifest manifest, WeeklyProductionAssetManifest? productionManifest, List<string> warnings)
    {
        var shortform = episodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase);
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastStartByAsset = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var segments = new List<FinalRenderSegment>();
        var previousLongformFamily = string.Empty;
        var currentLongformFamilyRun = 0;
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
            var segmentDuration = Math.Max(0.001d, segment.DurationSeconds);
            var segmentDurationFloor = Math.Max(1, (int)Math.Floor(segmentDuration));
            var shotCount = Math.Max(1, (int)Math.Ceiling(segmentDuration / maxShotDuration));
            var preferred = shortform
                ? Math.Max(1, Math.Min(pool.Count, (int)Math.Ceiling(segmentDuration / Math.Max(1, maxShotDuration))))
                : Math.Min(pool.Count, Math.Max(1, (int)Math.Ceiling(segmentDuration / 7d)));
            shotCount = Math.Max(shotCount, preferred);
            if (!shortform && (segment.SegmentType is "HeroEvent" or "StrongestEvent") && pool.Any(IsWesternGroupingAsset)) shotCount = Math.Max(shotCount, Math.Min(Math.Max(3, Math.Min(pool.Count, 6)), segmentDurationFloor));
            if (!shortform && segment.SegmentType.Equals("MoonHighlights", StringComparison.OrdinalIgnoreCase) && pool.Any(asset => IsMoonHeroPath(asset.AssetPath + " " + asset.AssetId))) shotCount = Math.Max(shotCount, Math.Min(3, segmentDurationFloor));
            if (!shortform && segment.SegmentType.Equals("PlanetHighlights", StringComparison.OrdinalIgnoreCase) && pool.Any(IsWesternGroupingAsset)) shotCount = Math.Max(shotCount, Math.Min(Math.Max(2, Math.Min(pool.Count, 5)), segmentDurationFloor));
            if (!shortform && segment.SegmentType.Equals("WeeklySkyOverview", StringComparison.OrdinalIgnoreCase)) shotCount = Math.Max(shotCount, Math.Min(Math.Max(4, Math.Min(pool.Count, 5)), segmentDurationFloor));
            if (!shortform && segment.SegmentType.Equals("BestObservationWindow", StringComparison.OrdinalIgnoreCase)) shotCount = Math.Max(shotCount, Math.Min(Math.Max(3, Math.Min(pool.Count, 4)), segmentDurationFloor));
            if (!shortform && segment.SegmentType.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase) && pool.Any(IsExpandedAstrophotographyAsset)) shotCount = Math.Max(shotCount, Math.Min(Math.Max(1, Math.Min(pool.Count, 3)), segmentDurationFloor));
            if (shortform && pool.Any(IsWesternGroupingAsset)) shotCount = Math.Max(shotCount, Math.Min(2, segmentDurationFloor));

            var orderedPool = shortform ? BuildShortformAssetSequence(pool, shotCount) : pool;
            var baseDuration = segmentDuration / shotCount;
            var cursor = segment.StartSecond;
            var shots = new List<FinalRenderShot>();
            var segmentUsedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < shotCount; i++)
            {
                var duration = baseDuration;
                var start = cursor;
                var rawAsset = shortform
                    ? PickAsset(orderedPool, usage, 1, i)
                    : PickLongformPolishedAsset(orderedPool, usage, segmentUsedAssets, lastStartByAsset, previousLongformFamily, currentLongformFamilyRun, start, i);
                var asset = shortform ? ResolveShortformVerticalVariant(rawAsset) : rawAsset;
                usage[asset.AssetPath] = usage.TryGetValue(asset.AssetPath, out var count) ? count + 1 : 1;
                segmentUsedAssets.Add(asset.AssetPath);
                lastStartByAsset[asset.AssetPath] = start;
                var end = i == shotCount - 1 ? segment.EndSecond : cursor + duration;
                var transitionIn = i == 0 ? (segment.StartSecond == 0 ? "FadeIn" : "CrossFade") : ResolveRenderTransition(shots[^1].AssetType, asset.AssetType, segment.SegmentType, shortform);
                var next = orderedPool[(i + 1) % orderedPool.Count];
                var transitionOut = i == shotCount - 1 ? "FadeOut" : ResolveRenderTransition(asset.AssetType, next.AssetType, segment.SegmentType, shortform);
                shots.Add(new FinalRenderShot(i + 1, asset.AssetId, asset.AssetType, asset.AssetPath, start, end, Math.Max(1, end - start), transitionIn, transitionOut, ResolveRenderMotion(asset.AssetType, segment.SegmentType), i == 0 ? $"render-refined primary visual for {segment.SegmentType}" : $"render-refined supporting visual variety for {segment.SegmentType}"));
                if (!shortform)
                {
                    var family = ResolveLayoutFamily(shots[^1]);
                    if (family.Equals(previousLongformFamily, StringComparison.OrdinalIgnoreCase)) currentLongformFamilyRun++;
                    else
                    {
                        previousLongformFamily = family;
                        currentLongformFamilyRun = 1;
                    }
                }
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
        var score = segmentType switch
        {
            "OpeningHook" or "ShortHook" => ScoreByPreference(haystack,
                ("cinematic_weekly_sky_reveal", 130), ("fast_cinematic_sky_hook", 115), ("weekly-overview", 80), ("wide", 75)),
            "WeeklySkyOverview" => ScoreByPreference(haystack,
                ("weekly-overview-timeline", 130), ("visibility-calendar", 125), ("cosmic_retention_reset", 95), ("NASA", 85), ("JWST", 85), ("/nasa/", 85), ("/jwst/", 85), ("wide", 80)),
            "HeroEvent" or "StrongestEvent" => ScoreByPreference(haystack,
                ("western_planet_grouping_scene/01_horizon_context", 160), ("western_planet_grouping_scene/02_balanced_story_frame", 155), ("western_planet_grouping_scene/03_alignment_wide", 150), ("western_planet_grouping_scene", 145), ("hero-event-card", 120), ("cinematic_weekly_sky_reveal", 95), ("NASA", 80), ("JWST", 80), ("/nasa/", 80), ("/jwst/", 80), ("moon_hero_scene", 45)),
            "MoonHighlights" => ScoreByPreference(haystack,
                ("moon_hero_scene/01", 150), ("moon_hero_scene/02", 145), ("moon_hero_scene/03", 140), ("moon_hero_scene", 135), ("where-to-look-card", 120), ("moon", 100), ("cosmic_retention_reset", 85)),
            "PlanetHighlights" => ScoreByPreference(haystack,
                ("western_planet_grouping_scene/01_horizon_context", 160), ("western_planet_grouping_scene/02_balanced_story_frame", 155), ("western_planet_grouping_scene/03_alignment_wide", 150), ("western_planet_grouping_scene", 145), ("where-to-look-card", 110), ("NASA", 95), ("JWST", 95), ("/nasa/", 95), ("/jwst/", 95), ("planet", 90), ("moon_hero_scene", -1000)),
            "BestObservationWindow" => ScoreByPreference(haystack,
                ("best-observation-window-card", 150), ("best-time-card", 145), ("where-to-look-card", 130), ("horizon", 100), ("weekly-overview-timeline", 10)),
            "AstrophotographyTip" => ScoreByPreference(haystack,
                ("astrophotography_target_scene/01_balanced_story_frame", 170), ("astrophotography_target_scene", 160), ("ExpandedStellarium", 150), ("cosmic_retention_reset", 120), ("where-to-look-card", 90)),
            "RetentionReset" => ScoreByPreference(haystack, ("cosmic_retention_reset", 160), ("AICinematic", 90)),
            "WeeklySummary" => ScoreByPreference(haystack,
                ("cosmic_closing_background", 160), ("weekly-summary-card", 140), ("shortform_call_to_action_background", 100), ("fast_cinematic_sky_hook", 5), ("wide", 80)),
            "CallToAction" => ScoreByPreference(haystack,
                ("shortform_call_to_action_background", 150), ("call-to-action-card", 140), ("AICinematic", 90)),
            _ => 0
        };
        return score;
    }

    private static int ScoreByPreference(string haystack, params (string Needle, int Score)[] preferences)
        => preferences.Where(p => haystack.Contains(p.Needle, StringComparison.OrdinalIgnoreCase)).Select(p => p.Score).DefaultIfEmpty(0).Max();

    private static int GenericAssetScore(string segmentType, RenderAssetCandidate asset)
        => asset.AssetType switch
        {
            "AICinematic" => 70,
            "MotionGraphic" => 65,
            "Stellarium" => segmentType.Contains("Moon", StringComparison.OrdinalIgnoreCase) ? 80 : 60,
            "ExpandedStellarium" => 55,
            _ => 30
        };

    private static IReadOnlyList<RenderAssetCandidate> BuildShortformAssetSequence(IReadOnlyList<RenderAssetCandidate> pool, int shotCount)
    {
        var sequence = new List<RenderAssetCandidate>();
        AddFirst(sequence, pool, IsFastCinematicSkyHookAsset);
        AddFirst(sequence, pool, IsWesternGroupingAsset);
        AddFirst(sequence, pool, asset => ContainsAny(asset.AssetPath + " " + asset.AssetId, "hero-event-card", "where-to-look-card"));
        AddFirst(sequence, pool, asset => IsWesternGroupingAsset(asset) && !sequence.Any(x => x.AssetPath.Equals(asset.AssetPath, StringComparison.OrdinalIgnoreCase)));
        AddFirst(sequence, pool, IsShortformCallToActionAsset);
        foreach (var asset in pool)
        {
            if (!sequence.Any(x => x.AssetPath.Equals(asset.AssetPath, StringComparison.OrdinalIgnoreCase))) sequence.Add(asset);
        }
        return sequence.Take(Math.Max(shotCount, Math.Min(sequence.Count, pool.Count))).ToList();
    }

    private static void AddFirst(List<RenderAssetCandidate> target, IReadOnlyList<RenderAssetCandidate> pool, Func<RenderAssetCandidate, bool> predicate)
    {
        var asset = pool.FirstOrDefault(predicate);
        if (asset is not null && !target.Any(x => x.AssetPath.Equals(asset.AssetPath, StringComparison.OrdinalIgnoreCase))) target.Add(asset);
    }


    private static RenderAssetCandidate ResolveShortformVerticalVariant(RenderAssetCandidate asset)
    {
        if (!IsMotionGraphicPath(asset.AssetPath + " " + asset.AssetId)) return asset;
        var directory = Path.GetDirectoryName(asset.AssetPath);
        if (string.IsNullOrWhiteSpace(directory)) return asset;
        var fileName = Path.GetFileNameWithoutExtension(asset.AssetPath);
        var extension = Path.GetExtension(asset.AssetPath);
        if (fileName.EndsWith("-vertical", StringComparison.OrdinalIgnoreCase)) return asset;
        var verticalPath = Path.Combine(directory, $"{fileName}-vertical{extension}");
        return File.Exists(verticalPath) ? asset with { AssetPath = verticalPath, AssetId = asset.AssetId.EndsWith("-vertical", StringComparison.OrdinalIgnoreCase) ? asset.AssetId : $"{asset.AssetId}-vertical" } : asset;
    }

    private static string ResolveShortformLayoutMode(FinalRenderShot shot)
    {
        var haystack = shot.AssetPath + " " + shot.AssetId + " " + shot.AssetType;
        if (IsTextSafeContainAsset(shot)) return "ContainWithBackground";
        if (shot.AssetType.Equals("NASA", StringComparison.OrdinalIgnoreCase) || shot.AssetType.Equals("JWST", StringComparison.OrdinalIgnoreCase) || ContainsAny(haystack, "/nasa/", "/jwst/")) return "ContainWithBackground";
        if (ContainsAny(haystack, "western_planet_grouping_scene", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide")) return "ContainBlurBackground";
        if (shot.AssetType.Equals("Stellarium", StringComparison.OrdinalIgnoreCase) || shot.AssetType.Equals("ExpandedStellarium", StringComparison.OrdinalIgnoreCase) || shot.AssetPath.Contains("stellarium", StringComparison.OrdinalIgnoreCase)) return "SmartCropVertical";
        return "VerticalCinematicCrop";
    }

    private static bool IsTextSafeContainAsset(FinalRenderShot shot)
    {
        var haystack = shot.AssetPath + " " + shot.AssetId + " " + shot.AssetType;
        return shot.AssetType.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase)
            || shot.AssetType.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(haystack, "motion-graphics", "educational-overlays", "hero-event-card", "best-time-card", "where-to-look-card", "call-to-action-card", "best-observation-window-card", "weekly-summary-card", "visibility-calendar");
    }

    private static bool UsesVerticalVariant(FinalRenderShot shot)
        => IsMotionGraphicPath(shot.AssetPath + " " + shot.AssetId) && ContainsAny(Path.GetFileNameWithoutExtension(shot.AssetPath), "-vertical");

    private static bool UsesVerticalFallbackContain(FinalRenderShot shot)
        => IsTextSafeContainAsset(shot) && !UsesVerticalVariant(shot) && ResolveShortformLayoutMode(shot).Equals("ContainWithBackground", StringComparison.OrdinalIgnoreCase);

    private static string ResolveLayoutFamily(FinalRenderShot shot)
    {
        var haystack = shot.AssetPath + " " + shot.AssetId + " " + shot.AssetType;
        if (IsWesternGroupingPath(haystack)) return "western_planet_grouping_scene";
        if (IsMoonHeroPath(haystack)) return "moon_hero_scene";
        if (shot.AssetType.Equals("AICinematic", StringComparison.OrdinalIgnoreCase) || haystack.Contains("ai-cinematic", StringComparison.OrdinalIgnoreCase)) return "AICinematic";
        if (shot.AssetType.Equals("NASA", StringComparison.OrdinalIgnoreCase) || haystack.Contains("/nasa/", StringComparison.OrdinalIgnoreCase)) return "NASA";
        if (shot.AssetType.Equals("JWST", StringComparison.OrdinalIgnoreCase) || haystack.Contains("/jwst/", StringComparison.OrdinalIgnoreCase)) return "JWST";
        if (IsMotionGraphicPath(haystack)) return "MotionGraphic";
        if (shot.AssetType.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase) || haystack.Contains("educational-overlays", StringComparison.OrdinalIgnoreCase)) return "EducationalOverlay";
        if (shot.AssetType.Equals("ExpandedStellarium", StringComparison.OrdinalIgnoreCase) || IsExpandedAstrophotographyPath(haystack)) return "ExpandedStellarium";
        if (shot.AssetType.Equals("Stellarium", StringComparison.OrdinalIgnoreCase) || haystack.Contains("stellarium", StringComparison.OrdinalIgnoreCase)) return "Stellarium";
        return shot.AssetType;
    }

    private static bool IsMotionGraphicPath(string value)
        => ContainsAny(value, "motion-graphics", "hero-event-card", "best-time-card", "where-to-look-card", "call-to-action-card", "best-observation-window-card", "weekly-summary-card", "visibility-calendar");

    private static bool IsWesternGroupingAsset(RenderAssetCandidate asset) => IsWesternGroupingPath(asset.AssetPath) || IsWesternGroupingPath(asset.AssetId);
    private static bool IsExpandedAstrophotographyAsset(RenderAssetCandidate asset) => IsExpandedAstrophotographyPath(asset.AssetPath) || IsExpandedAstrophotographyPath(asset.AssetId) || asset.AssetType.Equals("ExpandedStellarium", StringComparison.OrdinalIgnoreCase);
    private static bool IsFastCinematicSkyHookAsset(RenderAssetCandidate asset) => ContainsAny(asset.AssetPath + " " + asset.AssetId, "fast_cinematic_sky_hook");
    private static bool IsShortformCallToActionAsset(RenderAssetCandidate asset) => ContainsAny(asset.AssetPath + " " + asset.AssetId, "shortform_call_to_action_background");
    private static bool IsWesternGroupingPath(string value) => ContainsAny(value, "western_planet_grouping_scene", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide");
    private static bool IsMoonHeroPath(string value) => value.Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase);
    private static bool IsExpandedAstrophotographyPath(string value) => ContainsAny(value, "astrophotography_target_scene", "ExpandedStellarium");


    private static RenderAssetCandidate PickLongformPolishedAsset(
        IReadOnlyList<RenderAssetCandidate> pool,
        Dictionary<string, int> usage,
        HashSet<string> segmentUsedAssets,
        Dictionary<string, double> lastStartByAsset,
        string previousFamily,
        int currentFamilyRun,
        double startSecond,
        int index)
    {
        var ordered = Enumerable.Range(0, pool.Count).Select(offset => pool[(index + offset) % pool.Count]).ToList();
        var strict = ordered.FirstOrDefault(candidate =>
            !segmentUsedAssets.Contains(candidate.AssetPath) &&
            (!usage.TryGetValue(candidate.AssetPath, out var count) || count < 2) &&
            (!lastStartByAsset.TryGetValue(candidate.AssetPath, out var lastStart) || startSecond - lastStart >= 60) &&
            (currentFamilyRun < 3 || !ResolveLayoutFamily(candidate).Equals(previousFamily, StringComparison.OrdinalIgnoreCase)));
        if (strict is not null) return strict;

        var noSegmentRepeat = ordered.FirstOrDefault(candidate => !segmentUsedAssets.Contains(candidate.AssetPath));
        if (noSegmentRepeat is not null) return noSegmentRepeat;

        var noFamilyOverflow = ordered.FirstOrDefault(candidate => currentFamilyRun < 3 || !ResolveLayoutFamily(candidate).Equals(previousFamily, StringComparison.OrdinalIgnoreCase));
        return noFamilyOverflow ?? ordered[0];
    }

    private static string ResolveLayoutFamily(RenderAssetCandidate asset)
    {
        var synthetic = new FinalRenderShot(0, asset.AssetId, asset.AssetType, asset.AssetPath, 0, 1, 1, "", "", "", "");
        return ResolveLayoutFamily(synthetic);
    }

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

    private static IEnumerable<string> BuildFfmpegArguments(FinalRenderEpisodeTimeline timeline, string? audioPath, bool audioAttached, WeeklyEpisodeRenderContract contract, string outputPath, bool debugStoryboard, string? debugFontPath)
    {
        var portrait = outputPath.Contains("shortform", StringComparison.OrdinalIgnoreCase);
        var width = portrait ? 1080 : 1920;
        var height = portrait ? 1920 : 1080;
        var fps = 30;
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
        yield return BuildFilterComplex(shots, width, height, fps, debugStoryboard, debugFontPath);
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

    private static string BuildFilterComplex(IReadOnlyList<FinalRenderShot> shots, int width, int height, int fps, bool debugStoryboard, string? debugFontPath)
    {
        if (shots.Count == 0) return string.Empty;
        var parts = new List<string>();
        for (var i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            var frames = Math.Max(1, (int)Math.Ceiling(shot.DurationSeconds * fps));
            var zoom = BuildZoomExpression(shot.MotionEffect);
            var pan = BuildPanExpression(shot.MotionEffect);
            var fade = BuildShotFadeFilters(shot, fps);
            var overlay = debugStoryboard && !string.IsNullOrWhiteSpace(debugFontPath) ? BuildDebugStoryboardOverlay(shot, i, debugFontPath) : string.Empty;
            parts.Add($"[{i}:v]scale={width * 2}:{height * 2}:force_original_aspect_ratio=increase,crop={width * 2}:{height * 2},zoompan=z='{zoom}':x='{pan.X}':y='{pan.Y}':d={frames}:s={width}x{height}:fps={fps},trim=duration={shot.DurationSeconds},setpts=PTS-STARTPTS{fade}{overlay},format=yuv420p[v{i}]");
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

    private static double GetTransitionDurationSeconds(string transitionOut, string transitionIn, double previousDuration, double currentDuration)
    {
        var name = transitionOut.Equals("FadeOut", StringComparison.OrdinalIgnoreCase) ? transitionIn : transitionOut;
        var requested = name.Equals("SlowDissolve", StringComparison.OrdinalIgnoreCase) ? 1.5 : 0.6;
        return Math.Min(requested, Math.Max(0.1, Math.Min(previousDuration, currentDuration) / 3.0));
    }

    private static IEnumerable<string> BuildFfmpegArguments(string concatPath, string? audioPath, bool audioAttached, WeeklyEpisodeRenderContract contract, string outputPath)
    {
        var portrait = outputPath.Contains("shortform", StringComparison.OrdinalIgnoreCase);
        var width = portrait ? 1080 : 1920;
        var height = portrait ? 1920 : 1080;
        var fps = 30;
        yield return "-y";
        yield return "-f";
        yield return "concat";
        yield return "-safe";
        yield return "0";
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

    private static ResolvedRenderShotPlan BuildResolvedShotPlan(Guid pipelineRunId, IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans)
        => new(
            pipelineRunId,
            DateTime.UtcNow,
            plans.Select(plan =>
            {
                var timeline = File.Exists(plan.ConcatFilePath)
                    ? JsonSerializer.Deserialize<FinalRenderEpisodeTimeline>(File.ReadAllText(plan.ConcatFilePath), JsonOptions)
                    : null;
                return new ResolvedRenderEpisodeShotPlan(
                    plan.EpisodeType,
                    timeline?.ActualDurationSeconds ?? 0,
                    (timeline?.Segments ?? []).Select(segment => new ResolvedRenderSegmentShotPlan(
                        plan.EpisodeType,
                        segment.SegmentId,
                        segment.SegmentType,
                        segment.StartSecond,
                        segment.EndSecond,
                        segment.DurationSeconds,
                        (segment.Shots ?? []).Select(shot => new ResolvedRenderShotPlanEntry(
                            shot.ShotNumber,
                            shot.AssetId,
                            shot.AssetType,
                            shot.AssetPath,
                            shot.StartSecond,
                            shot.EndSecond,
                            shot.DurationSeconds,
                            shot.TransitionIn,
                            shot.TransitionOut,
                            shot.MotionEffect,
                            shot.Purpose,
                            plan.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase) ? ResolveShortformLayoutMode(shot) : "LandscapeFill",
                            plan.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase) && UsesVerticalVariant(shot),
                            plan.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase) && UsesVerticalFallbackContain(shot))).ToList())).ToList());
            }).ToList());

    private static RenderVisualSelectionReport BuildVisualSelectionReport(IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans, IReadOnlyList<string> renderWarnings, IReadOnlyList<string> renderErrors)
    {
        var warnings = new List<string>(renderWarnings);
        var errors = new List<string>(renderErrors);
        var shotRows = LoadResolvedShotRows(plans).ToList();
        var longformRows = shotRows.Where(x => x.EpisodeType.Equals("longform", StringComparison.OrdinalIgnoreCase)).ToList();
        var shortformRows = shotRows.Where(x => x.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase)).ToList();
        var allShots = shotRows.Select(x => x.Shot).ToList();
        var assetUsage = allShots.GroupBy(x => x.AssetPath, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var aiUsage = allShots.Where(x => x.AssetType.Equals("AICinematic", StringComparison.OrdinalIgnoreCase) || x.AssetPath.Contains("ai-cinematic", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => ResolveAiCinematicKey(x), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var heroGrouping = longformRows.Count(x => (x.Segment.SegmentType is "HeroEvent" or "StrongestEvent") && IsWesternGroupingPath(x.Shot.AssetPath + " " + x.Shot.AssetId));
        var planetGrouping = longformRows.Count(x => x.Segment.SegmentType.Equals("PlanetHighlights", StringComparison.OrdinalIgnoreCase) && IsWesternGroupingPath(x.Shot.AssetPath + " " + x.Shot.AssetId));
        var shortGrouping = shortformRows.Count(x => IsWesternGroupingPath(x.Shot.AssetPath + " " + x.Shot.AssetId));
        var moonHero = longformRows.Count(x => IsMoonHeroPath(x.Shot.AssetPath + " " + x.Shot.AssetId));
        var expanded = longformRows.Count(x => IsExpandedAstrophotographyPath(x.Shot.AssetPath + " " + x.Shot.AssetId));
        var stellariumRows = shotRows.Where(x => x.Shot.AssetType.Equals("Stellarium", StringComparison.OrdinalIgnoreCase) || x.Shot.AssetType.Equals("ExpandedStellarium", StringComparison.OrdinalIgnoreCase) || x.Shot.AssetPath.Contains("stellarium", StringComparison.OrdinalIgnoreCase)).ToList();
        var moonOnly = stellariumRows.Count > 0 && !stellariumRows.Any(x => IsWesternGroupingPath(x.Shot.AssetPath + " " + x.Shot.AssetId));
        var maxLong = longformRows.Count == 0 ? 0 : longformRows.Max(x => x.Shot.DurationSeconds);
        var maxShort = shortformRows.Count == 0 ? 0 : shortformRows.Max(x => x.Shot.DurationSeconds);
        var repeatValidation = BuildAssetRepeatValidation(longformRows, shortformRows);
        var familyValidation = BuildAssetFamilyDistributionValidation(shotRows);
        var sameAssetRepeatedTooMuch = repeatValidation.HardFailureCount > 0;
        var repeated = repeatValidation.WarningCount + repeatValidation.HardFailureCount;
        var weeklyOverviewUsage = allShots.Count(x => ContainsAny(x.AssetPath + " " + x.AssetId, "weekly-overview-timeline"));
        var fastHookUsage = allShots.Count(x => ContainsAny(x.AssetPath + " " + x.AssetId, "fast_cinematic_sky_hook"));
        var longPacing = longformRows.All(x => x.Shot.DurationSeconds <= GetMaxShotDurationSeconds(x.Segment.SegmentType, false));
        var shortPacing = shortformRows.All(x => x.Shot.DurationSeconds <= GetMaxShotDurationSeconds(x.Segment.SegmentType, true));
        var aiPassed = fastHookUsage <= 2 && aiUsage.Keys.Count(k => aiUsage[k] > 0) >= Math.Min(4, Math.Max(1, aiUsage.Count));
        var bestObservationRows = longformRows.Where(x => x.Segment.SegmentType.Equals("BestObservationWindow", StringComparison.OrdinalIgnoreCase)).ToList();
        var bestObservationOnlyOverview = bestObservationRows.Count > 0 && bestObservationRows.All(x => ContainsAny(x.Shot.AssetPath + " " + x.Shot.AssetId, "weekly-overview-timeline"));
        var motionGraphicUsage = allShots.Count(x => x.AssetType.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase) || x.AssetPath.Contains("motion-graphics", StringComparison.OrdinalIgnoreCase));
        var motionPassed = weeklyOverviewUsage <= 2 && !bestObservationOnlyOverview && motionGraphicUsage >= Math.Min(5, allShots.Count);
        var longformRequested = longformRows.Count > 0;
        var shortformRequested = shortformRows.Count > 0;
        var stellariumPassed = (!longformRequested || (moonHero >= Math.Min(3, longformRows.Count) && heroGrouping >= 3 && expanded >= 1)) && (!shortformRequested || shortGrouping >= 2) && !moonOnly;
        var nasaUsage = longformRows.Count(x => x.Shot.AssetType.Equals("NASA", StringComparison.OrdinalIgnoreCase) || x.Shot.AssetPath.Contains("/nasa/", StringComparison.OrdinalIgnoreCase));
        var jwstUsage = longformRows.Count(x => x.Shot.AssetType.Equals("JWST", StringComparison.OrdinalIgnoreCase) || x.Shot.AssetPath.Contains("/jwst/", StringComparison.OrdinalIgnoreCase));
        var moonHeroDuration = longformRows.Where(x => IsMoonHeroPath(x.Shot.AssetPath + " " + x.Shot.AssetId)).Sum(x => x.Shot.DurationSeconds);
        var longformDuration = Math.Max(1, longformRows.Sum(x => x.Shot.DurationSeconds));
        var moonHeroPercent = Math.Round(moonHeroDuration * 100.0 / longformDuration, 2);
        var visualPassed = (!longformRequested || (heroGrouping >= 3 && planetGrouping >= 2 && expanded >= 1 && nasaUsage >= 2 && jwstUsage >= 1 && moonHeroPercent <= 30)) && (!shortformRequested || shortGrouping >= 2) && !moonOnly;

        if (visualPassed) repeatValidation = DowngradeAssetAvailabilityRepeatFailures(repeatValidation, longformRows, shortformRows);
        sameAssetRepeatedTooMuch = repeatValidation.HardFailureCount > 0;
        repeated = repeatValidation.WarningCount + repeatValidation.HardFailureCount;

        if (longformRequested && heroGrouping < 3) errors.Add("HeroEvent must include at least 3 western_planet_grouping_scene frames when those files exist.");
        if (longformRequested && planetGrouping < 2) errors.Add("PlanetHighlights must include at least 2 western_planet_grouping_scene frames when those files exist.");
        if (shortformRequested && shortGrouping < 2) errors.Add("Shortform must include at least 2 western_planet_grouping_scene frames when those files exist.");
        if (longformRequested && expanded < 1) errors.Add("AstrophotographyTip must include an ExpandedStellarium astrophotography_target_scene frame.");
        if (moonOnly) errors.Add("Moon-only Stellarium visual selection detected; western_planet_grouping_scene frames are required.");
        if (longformRequested && nasaUsage < 2) errors.Add("Longform must include at least 2 NASA assets when available.");
        if (longformRequested && jwstUsage < 1) errors.Add("Longform must include at least 1 JWST asset when available.");
        if (longformRequested && moonHeroPercent > 30) errors.Add("moon_hero_scene visual duration must not exceed 30% of longform visual duration.");
        warnings.AddRange(repeatValidation.Warnings);
        warnings.AddRange(familyValidation.Warnings);

        var assetRepeatLimitPassed = repeatValidation.Passed;
        var renderVisualDiversityReady = assetRepeatLimitPassed && familyValidation.Passed && aiPassed && motionPassed && stellariumPassed && shortPacing && longPacing && visualPassed && errors.Count == 0;

        return new RenderVisualSelectionReport(
            heroGrouping,
            planetGrouping,
            shortGrouping,
            moonHero,
            expanded,
            moonOnly,
            maxLong,
            maxShort,
            sameAssetRepeatedTooMuch,
            repeated,
            "weighted",
            4,
            2,
            repeatValidation.HardFailureCount,
            repeatValidation.WarningCount,
            repeatValidation.MaxUsageCount,
            repeatValidation.MaxDurationPercent,
            repeatValidation.MaxConsecutiveCount,
            familyValidation.DurationPercentages,
            assetRepeatLimitPassed,
            familyValidation.Passed,
            renderVisualDiversityReady,
            weeklyOverviewUsage,
            fastHookUsage,
            aiPassed,
            motionPassed,
            stellariumPassed,
            shortPacing,
            longPacing,
            visualPassed,
            nasaUsage,
            jwstUsage,
            motionGraphicUsage,
            aiUsage.Keys.Count(k => aiUsage[k] > 0),
            moonHeroPercent,
            assetUsage,
            aiUsage,
            warnings,
            errors);
    }

    private sealed record AssetRepeatValidationResult(
        bool Passed,
        int HardFailureCount,
        int WarningCount,
        int AvailabilityDowngradableFailureCount,
        int MaxUsageCount,
        double MaxDurationPercent,
        int MaxConsecutiveCount,
        IReadOnlyList<string> Warnings);

    private sealed record AssetFamilyDistributionValidationResult(
        bool Passed,
        IReadOnlyDictionary<string, double> DurationPercentages,
        IReadOnlyList<string> Warnings);

    private static AssetRepeatValidationResult BuildAssetRepeatValidation(
        IReadOnlyList<(string EpisodeType, FinalRenderSegment Segment, FinalRenderShot Shot)> longformRows,
        IReadOnlyList<(string EpisodeType, FinalRenderSegment Segment, FinalRenderShot Shot)> shortformRows)
    {
        var warnings = new List<string>();
        var hardFailures = 0;
        var warningCount = 0;
        var availabilityDowngradableFailures = 0;
        var maxUsage = 0;
        var maxDurationPercent = 0.0;
        var maxConsecutive = 0;

        EvaluateEpisodeAssetRepeats(longformRows, "longform", 4, 5, 2, true, warnings, ref hardFailures, ref warningCount, ref availabilityDowngradableFailures, ref maxUsage, ref maxDurationPercent, ref maxConsecutive);
        EvaluateEpisodeAssetRepeats(shortformRows, "shortform", 2, 2, 1, false, warnings, ref hardFailures, ref warningCount, ref availabilityDowngradableFailures, ref maxUsage, ref maxDurationPercent, ref maxConsecutive);

        return new AssetRepeatValidationResult(hardFailures == 0, hardFailures, warningCount, availabilityDowngradableFailures, maxUsage, Math.Round(maxDurationPercent, 2), maxConsecutive, warnings);
    }

    private static void EvaluateEpisodeAssetRepeats(
        IReadOnlyList<(string EpisodeType, FinalRenderSegment Segment, FinalRenderShot Shot)> rows,
        string episodeType,
        int allowedUses,
        int hardFailureUses,
        int allowedConsecutiveUses,
        bool longform,
        List<string> warnings,
        ref int hardFailures,
        ref int warningCount,
        ref int availabilityDowngradableFailures,
        ref int maxUsage,
        ref double maxDurationPercent,
        ref int maxConsecutive)
    {
        if (rows.Count == 0) return;

        var episodeDuration = Math.Max(1, rows.Sum(x => x.Shot.DurationSeconds));
        foreach (var group in rows.GroupBy(x => x.Shot.AssetPath, StringComparer.OrdinalIgnoreCase))
        {
            var usage = group.Count();
            var durationPercent = group.Sum(x => x.Shot.DurationSeconds) * 100.0 / episodeDuration;
            maxUsage = Math.Max(maxUsage, usage);
            maxDurationPercent = Math.Max(maxDurationPercent, durationPercent);

            var reusableBackground = group.Any(x => IsReusableGlobalBackground(x.Shot));
            var countFailure = longform ? usage > hardFailureUses && !reusableBackground : usage > allowedUses;
            var durationFailure = durationPercent > 18.0;

            if (countFailure || durationFailure)
            {
                hardFailures++;
                if (countFailure && !durationFailure) availabilityDowngradableFailures++;
                warnings.Add($"{episodeType} asset repeat hard failure for '{group.Key}': uses={usage}, durationPercent={Math.Round(durationPercent, 2)}.");
            }
            else if (usage > allowedUses)
            {
                warningCount++;
                warnings.Add($"{episodeType} controlled asset reuse warning for '{group.Key}': uses={usage}, durationPercent={Math.Round(durationPercent, 2)}.");
            }
        }

        var currentAsset = string.Empty;
        var currentCount = 0;
        foreach (var row in rows)
        {
            if (row.Shot.AssetPath.Equals(currentAsset, StringComparison.OrdinalIgnoreCase)) currentCount++;
            else
            {
                currentAsset = row.Shot.AssetPath;
                currentCount = 1;
            }

            maxConsecutive = Math.Max(maxConsecutive, currentCount);
            if (currentCount == allowedConsecutiveUses + 1)
            {
                hardFailures++;
                warnings.Add($"{episodeType} asset repeat hard failure: '{row.Shot.AssetPath}' appears in more than {allowedConsecutiveUses} consecutive shots.");
            }
        }
    }

    private static AssetRepeatValidationResult DowngradeAssetAvailabilityRepeatFailures(
        AssetRepeatValidationResult result,
        IReadOnlyList<(string EpisodeType, FinalRenderSegment Segment, FinalRenderShot Shot)> longformRows,
        IReadOnlyList<(string EpisodeType, FinalRenderSegment Segment, FinalRenderShot Shot)> shortformRows)
    {
        if (result.HardFailureCount == 0) return result;
        if (result.AvailabilityDowngradableFailureCount != result.HardFailureCount) return result;

        var longformAssetLimited = longformRows.Count > 0 && longformRows.Select(x => x.Shot.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 4;
        var shortformAssetLimited = shortformRows.Count > 0 && shortformRows.Select(x => x.Shot.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 2;
        if (!longformAssetLimited && !shortformAssetLimited) return result;

        var warnings = result.Warnings.Concat(["Asset repeat hard failures were downgraded to warnings because the episode has fewer available unique visual assets than the calibrated repeat threshold and visualDistributionPassed=true."]).ToList();
        return result with { Passed = true, WarningCount = result.WarningCount + result.HardFailureCount, HardFailureCount = 0, AvailabilityDowngradableFailureCount = 0, Warnings = warnings };
    }

    private static AssetFamilyDistributionValidationResult BuildAssetFamilyDistributionValidation(IReadOnlyList<(string EpisodeType, FinalRenderSegment Segment, FinalRenderShot Shot)> rows)
    {
        var totalDuration = Math.Max(1, rows.Sum(x => x.Shot.DurationSeconds));
        var families = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["moon_hero_scene"] = 0,
            ["western_planet_grouping_scene"] = 0,
            ["AICinematic"] = 0,
            ["NASA"] = 0,
            ["JWST"] = 0,
            ["MotionGraphic"] = 0,
            ["EducationalOverlay"] = 0,
            ["ExpandedStellarium"] = 0
        };

        foreach (var row in rows)
        {
            var shot = row.Shot;
            var haystack = shot.AssetPath + " " + shot.AssetId + " " + shot.AssetType + " " + shot.Purpose;
            if (IsMoonHeroPath(haystack)) families["moon_hero_scene"] += shot.DurationSeconds;
            if (IsWesternGroupingPath(haystack)) families["western_planet_grouping_scene"] += shot.DurationSeconds;
            if (shot.AssetType.Equals("AICinematic", StringComparison.OrdinalIgnoreCase) || shot.AssetPath.Contains("ai-cinematic", StringComparison.OrdinalIgnoreCase)) families["AICinematic"] += shot.DurationSeconds;
            if (shot.AssetType.Equals("NASA", StringComparison.OrdinalIgnoreCase) || shot.AssetPath.Contains("/nasa/", StringComparison.OrdinalIgnoreCase)) families["NASA"] += shot.DurationSeconds;
            if (shot.AssetType.Equals("JWST", StringComparison.OrdinalIgnoreCase) || shot.AssetPath.Contains("/jwst/", StringComparison.OrdinalIgnoreCase)) families["JWST"] += shot.DurationSeconds;
            if (shot.AssetType.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase) || shot.AssetPath.Contains("motion-graphics", StringComparison.OrdinalIgnoreCase)) families["MotionGraphic"] += shot.DurationSeconds;
            if (shot.AssetType.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase) || ContainsAny(haystack, "educational-overlay", "educational_overlay")) families["EducationalOverlay"] += shot.DurationSeconds;
            if (shot.AssetType.Equals("ExpandedStellarium", StringComparison.OrdinalIgnoreCase) || IsExpandedAstrophotographyPath(haystack)) families["ExpandedStellarium"] += shot.DurationSeconds;
        }

        var percentages = families.ToDictionary(x => x.Key, x => Math.Round(x.Value * 100.0 / totalDuration, 2), StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        CheckFamilyLimit(percentages, "moon_hero_scene", 30, warnings);
        CheckFamilyLimit(percentages, "western_planet_grouping_scene", 35, warnings);
        CheckFamilyLimit(percentages, "AICinematic", 30, warnings);
        CheckFamilyLimit(percentages, "MotionGraphic", 35, warnings);
        CheckFamilyLimit(percentages, "EducationalOverlay", 18, warnings);
        CheckFamilyLimit(percentages, "ExpandedStellarium", 18, warnings);
        var nasaJwstPercent = percentages["NASA"] + percentages["JWST"];
        if (nasaJwstPercent > 30) warnings.Add($"NASA/JWST combined visual duration is {Math.Round(nasaJwstPercent, 2)}%, above the 30% calibrated family limit.");

        return new AssetFamilyDistributionValidationResult(warnings.Count == 0, percentages, warnings);
    }

    private static void CheckFamilyLimit(IReadOnlyDictionary<string, double> percentages, string family, double limit, List<string> warnings)
    {
        if (percentages.TryGetValue(family, out var percentage) && percentage > limit)
        {
            warnings.Add($"{family} visual duration is {percentage}%, above the {limit}% calibrated family limit.");
        }
    }

    private static bool IsReusableGlobalBackground(FinalRenderShot shot)
        => ContainsAny(shot.AssetPath + " " + shot.AssetId + " " + shot.Purpose, "reusable_global_background");

    private static RenderDiversityValidationReport BuildDiversityValidationReport(RenderVisualSelectionReport report)
    {
        var warnings = report.Warnings.ToList();
        var errors = report.Errors.ToList();
        var longformEvaluated = report.MaxLongformShotDurationSeconds > 0;
        var segmentAware = !longformEvaluated || (report.HeroEventWesternGroupingFrameCount >= 3 && report.PlanetHighlightsWesternGroupingFrameCount >= 2 && report.ExpandedAstrophotographyFrameCount >= 1);
        var hero = !longformEvaluated || report.HeroEventWesternGroupingFrameCount >= 3;
        var planet = !longformEvaluated || report.PlanetHighlightsWesternGroupingFrameCount >= 2;
        var moonDetection = !report.MoonOnlyStellariumDetected;
        var repeat = report.AssetRepeatLimitPassed;
        var shotDuration = report.MaxLongformShotDurationSeconds <= 14 && report.MaxShortformShotDurationSeconds <= 8;
        var ready = segmentAware && hero && planet && moonDetection && repeat && report.AssetFamilyDistributionPassed && report.AiCinematicDiversityPassed && report.MotionGraphicDiversityPassed && report.StellariumSceneBalancePassed && report.VisualDistributionPassed && shotDuration && report.ShortformPacingPassed && report.LongformPacingPassed && errors.Count == 0;
        return new RenderDiversityValidationReport(
            ready,
            segmentAware,
            hero,
            planet,
            moonDetection,
            repeat,
            report.AssetRepeatValidationMode,
            repeat,
            report.AssetFamilyDistributionPassed,
            report.SameAssetPathHardFailureCount,
            report.SameAssetPathWarningCount,
            report.MaxConsecutiveSameAssetPathCount,
            report.AiCinematicDiversityPassed,
            report.MotionGraphicDiversityPassed,
            shotDuration,
            report.ShortformPacingPassed,
            warnings,
            errors);
    }

    private static IEnumerable<(string EpisodeType, FinalRenderSegment Segment, FinalRenderShot Shot)> LoadResolvedShotRows(IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans)
    {
        foreach (var plan in plans)
        {
            if (!File.Exists(plan.ConcatFilePath)) continue;
            var timeline = JsonSerializer.Deserialize<FinalRenderEpisodeTimeline>(File.ReadAllText(plan.ConcatFilePath), JsonOptions);
            foreach (var segment in timeline?.Segments ?? [])
            foreach (var shot in segment.Shots ?? [])
            {
                yield return (plan.EpisodeType, segment, shot);
            }
        }
    }


    private sealed record LongformRepetitionPolishMetrics(bool Passed, int BackToBackRepeatCount, int SameAssetSameSegmentRepeatCount, int SameFamilyConsecutiveMax);
    private sealed record ShortformLayoutMetrics(bool VerticalLayoutPassed, bool SafeAreaPassed, int ContainLayoutCount, int SmartCropLayoutCount, int CroppedTextRiskCount);

    private static LongformRepetitionPolishMetrics CalculateLongformRepetitionPolish(IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans)
    {
        var rows = LoadResolvedShotRows(plans).Where(x => x.EpisodeType.Equals("longform", StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0) return new LongformRepetitionPolishMetrics(true, 0, 0, 0);

        var backToBack = 0;
        var currentFamily = string.Empty;
        var currentFamilyCount = 0;
        var maxFamily = 0;
        string? previousAsset = null;
        foreach (var row in rows)
        {
            if (previousAsset is not null && row.Shot.AssetPath.Equals(previousAsset, StringComparison.OrdinalIgnoreCase)) backToBack++;
            previousAsset = row.Shot.AssetPath;

            var family = ResolveLayoutFamily(row.Shot);
            if (family.Equals(currentFamily, StringComparison.OrdinalIgnoreCase)) currentFamilyCount++;
            else
            {
                currentFamily = family;
                currentFamilyCount = 1;
            }
            maxFamily = Math.Max(maxFamily, currentFamilyCount);
        }

        var sameSegment = rows
            .GroupBy(x => new { x.Segment.SegmentId, x.Segment.SegmentType })
            .Sum(group => group.GroupBy(x => x.Shot.AssetPath, StringComparer.OrdinalIgnoreCase).Sum(assetGroup => Math.Max(0, assetGroup.Count() - 1)));

        var spreadViolations = rows
            .GroupBy(x => x.Shot.AssetPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .Sum(group => group.OrderBy(x => x.Shot.StartSecond).Zip(group.OrderBy(x => x.Shot.StartSecond).Skip(1), (a, b) => b.Shot.StartSecond - a.Shot.StartSecond < 60 ? 1 : 0).Sum());

        var passed = backToBack == 0 && sameSegment == 0 && maxFamily <= 3 && spreadViolations == 0;
        return new LongformRepetitionPolishMetrics(passed, backToBack, sameSegment, maxFamily);
    }

    private static ShortformLayoutMetrics CalculateShortformLayoutMetrics(IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans)
    {
        var rows = LoadResolvedShotRows(plans).Where(x => x.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0) return new ShortformLayoutMetrics(true, true, 0, 0, 0);

        var contain = rows.Count(x => ResolveShortformLayoutMode(x.Shot) is "ContainWithBackground" or "ContainBlurBackground");
        var smartCrop = rows.Count(x => ResolveShortformLayoutMode(x.Shot) is "SmartCropVertical" or "VerticalCinematicCrop");
        var textRisk = rows.Count(x => IsTextSafeContainAsset(x.Shot) && ResolveShortformLayoutMode(x.Shot) != "ContainWithBackground");
        var unsafeCardFallback = rows.Count(x => IsTextSafeContainAsset(x.Shot) && !UsesVerticalVariant(x.Shot) && !UsesVerticalFallbackContain(x.Shot));
        textRisk += unsafeCardFallback;
        return new ShortformLayoutMetrics(true, textRisk == 0, contain, smartCrop, textRisk);
    }

    private static string ResolveAiCinematicKey(FinalRenderShot shot)
    {
        var haystack = shot.AssetPath + " " + shot.AssetId;
        foreach (var key in new[] { "fast_cinematic_sky_hook", "cinematic_weekly_sky_reveal", "cosmic_retention_reset", "cosmic_closing_background", "shortform_call_to_action_background" })
        {
            if (haystack.Contains(key, StringComparison.OrdinalIgnoreCase)) return key;
        }
        return shot.AssetId;
    }

    private static WeeklyRenderQualityReport BuildQualityReport(Guid pipelineRunId, IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans, IReadOnlyList<string> warnings)
    {
        var longformPlan = plans.FirstOrDefault(p => p.EpisodeType.Equals("longform", StringComparison.OrdinalIgnoreCase));
        var shortformPlan = plans.FirstOrDefault(p => p.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase));
        var longform = longformPlan?.QualityMetrics;
        var shortform = shortformPlan?.QualityMetrics;
        var metrics = plans.Select(p => p.QualityMetrics).ToList();
        var longformPolish = CalculateLongformRepetitionPolish(plans);
        var shortformLayout = CalculateShortformLayoutMetrics(plans);
        var longformReady = longform is null || (longform.PacingPassed && longform.VisualDistributionPassed && longformPolish.Passed);
        var shortformReady = shortform is null || (shortform.PacingPassed && shortform.VisualDistributionPassed && shortformLayout.VerticalLayoutPassed && shortformLayout.SafeAreaPassed);
        var videoOnlyReady = plans.Count > 0 && plans.All(p => p.UseStagedRendering) && longformReady && shortformReady;
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
            longformReady,
            shortformReady,
            videoOnlyReady,
            longformPolish.Passed,
            shortformLayout.VerticalLayoutPassed,
            shortformLayout.SafeAreaPassed,
            shortformLayout.ContainLayoutCount,
            shortformLayout.SmartCropLayoutCount,
            shortformLayout.CroppedTextRiskCount,
            longformPolish.BackToBackRepeatCount,
            longformPolish.SameAssetSameSegmentRepeatCount,
            longformPolish.SameFamilyConsecutiveMax,
            longformPlan?.RenderMode ?? "notRequested",
            shortformPlan?.RenderMode ?? "notRequested",
            metrics,
            warnings);
    }

    private static WeeklyExistingRunEpisodeQualityMetrics BuildEpisodeQualityMetrics(string episodeType, FinalRenderEpisodeTimeline timeline)
    {
        var shortform = episodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase);
        var shots = timeline.Segments.SelectMany(s => s.Shots.Select(shot => (Segment: s, Shot: shot))).ToList();
        var maxShot = shots.Count == 0 ? 0 : (int)Math.Ceiling(shots.Max(x => x.Shot.DurationSeconds));
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
        var containLayout = shortform ? shots.Count(x => ResolveShortformLayoutMode(x.Shot) is "ContainWithBackground" or "ContainBlurBackground") : 0;
        var smartCropLayout = shortform ? shots.Count(x => ResolveShortformLayoutMode(x.Shot) is "SmartCropVertical" or "VerticalCinematicCrop") : 0;
        var croppedTextRisk = shortform ? shots.Count(x => IsTextSafeContainAsset(x.Shot) && ResolveShortformLayoutMode(x.Shot) != "ContainWithBackground") : 0;
        return new WeeklyExistingRunEpisodeQualityMetrics(episodeType, maxShot, repeated, moonOnly, grouping, motionApplied, transitions, fallbackTransitions, fallbackMotion, pacing, distribution, containLayout, smartCropLayout, croppedTextRisk);
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
        "EducationalOverlay" => "EducationalOverlay",
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


    private async Task<(WeeklyRenderInputManifest Manifest, int TotalProductionAssetsDiscovered, int TotalRenderInputAssets, bool RenderInputHydrationPassed)> HydrateRenderInputManifestAsync(Guid pipelineRunId, string root, WeeklyRenderInputManifest existing, WeeklyProductionAssetManifest? productionManifest, FinalRenderTimeline timeline, CancellationToken cancellationToken)
    {
        var candidates = new List<RenderAssetCandidate>();
        var longformShots = (timeline.Longform?.Segments ?? [])
            .Where(segment => segment is not null)
            .SelectMany(segment => segment.Shots ?? [])
            .Where(shot => shot is not null)
            .ToList();
        var shortformShots = (timeline.Shortform?.Segments ?? [])
            .Where(segment => segment is not null)
            .SelectMany(segment => segment.Shots ?? [])
            .Where(shot => shot is not null)
            .ToList();
        var timelineShots = longformShots.Concat(shortformShots).ToList();

        candidates.AddRange((existing.Assets ?? [])
            .Where(asset => asset is not null)
            .Select(asset => new RenderAssetCandidate(asset.AssetId, NormalizeRenderAssetType(asset.AssetType), asset.AssetPath)));
        if (productionManifest is not null)
        {
            candidates.AddRange((productionManifest.SegmentBundles ?? [])
                .Where(bundle => bundle is not null)
                .SelectMany(bundle => bundle.AssignedVisualAssets ?? [])
                .Where(asset => asset is not null && asset.Exists && asset.ProductionReady)
                .Select(asset => new RenderAssetCandidate(NormalizeAssetId(asset.SourceType.ToString(), asset.FilePath, asset.AssetCode), NormalizeRenderAssetType(asset.SourceType.ToString()), asset.FilePath)));
        }
        candidates.AddRange(timelineShots
            .Select(shot => new RenderAssetCandidate(shot.AssetId, NormalizeRenderAssetType(shot.AssetType), shot.AssetPath)));
        foreach (var pattern in new[]
        {
            Path.Combine(root, "stellarium", "scenes"),
            Path.Combine(root, "ai-cinematic"),
            Path.Combine(root, "assets", "nasa"),
            Path.Combine(root, "assets", "jwst"),
            Path.Combine(root, "assets", "motion-graphics"),
            Path.Combine(root, "assets", "educational-overlays")
        })
        {
            if (!Directory.Exists(pattern)) continue;
            candidates.AddRange(Directory.EnumerateFiles(pattern, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedImagePath)
                .Select(path => new RenderAssetCandidate(NormalizeAssetId(ClassifyAssetTypeFromPath(root, path), path, Path.GetFileNameWithoutExtension(path)), NormalizeRenderAssetType(ClassifyAssetTypeFromPath(root, path)), path)));
        }

        var distinct = candidates.Where(a => !string.IsNullOrWhiteSpace(a.AssetPath) && File.Exists(a.AssetPath))
            .DistinctBy(a => Path.GetFullPath(a.AssetPath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a.AssetType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var assets = new List<WeeklyRenderInputAsset>();
        foreach (var asset in distinct)
        {
            var width = 0;
            var height = 0;
            var readable = false;
            var validationErrors = new List<string>();
            var fileInfo = new FileInfo(asset.AssetPath);
            try
            {
                var info = await Image.IdentifyAsync(asset.AssetPath, cancellationToken);
                if (info is not null)
                {
                    width = info.Width;
                    height = info.Height;
                    readable = width > 0 && height > 0;
                }
                else validationErrors.Add("Image could not be decoded.");
            }
            catch (Exception ex)
            {
                validationErrors.Add($"Image decode failed: {ex.Message}");
            }
            var usages = timelineShots.Where(shot => string.Equals(shot.AssetPath, asset.AssetPath, StringComparison.OrdinalIgnoreCase)).ToList();
            assets.Add(new WeeklyRenderInputAsset(asset.AssetId, asset.AssetType, asset.AssetPath, true, width, height, usages.Sum(s => s.DurationSeconds), longformShots.Any(shot => string.Equals(shot.AssetPath, asset.AssetPath, StringComparison.OrdinalIgnoreCase)), shortformShots.Any(shot => string.Equals(shot.AssetPath, asset.AssetPath, StringComparison.OrdinalIgnoreCase)), readable, fileInfo.Length, validationErrors));
        }
        var totalProduction = Math.Max(productionManifest?.TotalProductionImageAssetCount ?? 0, distinct.Count);
        var hydrationPassed = totalProduction == 0 || assets.Count >= Math.Ceiling(totalProduction * 0.8);
        var warnings = (existing.Warnings ?? []).Concat(hydrationPassed ? [] : [$"Render input hydration discovered {assets.Count} assets; expected at least 80% of {totalProduction} production assets."]).ToList();
        var errors = assets.SelectMany(a => a.ValidationErrors.Select(e => $"{a.AssetId}: {e}")).ToList();
        return (new WeeklyRenderInputManifest(pipelineRunId, DateTime.UtcNow, assets, assets.All(a => a.Exists), assets.All(a => a.Readable), warnings, errors, totalProduction, assets.Count, hydrationPassed), totalProduction, assets.Count, hydrationPassed);
    }

    private async Task<(WeeklyExistingRunFfmpegCommandReport Report, (string EpisodeType, WeeklyExistingRunEpisodeRenderReport RenderReport) Result)> ExecutePlanAsync(WeeklyExistingRunFfmpegCommandPlan plan, WeeklyExistingRunRenderRequest request, CancellationToken cancellationToken)
    {
        if (File.Exists(plan.OutputPath) && !request.OverwriteExisting)
        {
            var skippedInfo = new FileInfo(plan.OutputPath);
            return (new WeeklyExistingRunFfmpegCommandReport(plan.EpisodeType, plan.OutputPath, true, false, true, null, 0, plan.Command, null, ["Output already exists and overwriteExisting is false."], []), (plan.EpisodeType, new WeeklyExistingRunEpisodeRenderReport(true, false, true, plan.OutputPath, plan.QualityMetrics.MaxShotDurationSeconds, skippedInfo.Length, plan.AudioAttached)));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(plan.StderrPath) ?? ".");
        await File.WriteAllTextAsync(plan.StderrPath, string.Empty, cancellationToken);

        if (plan.UseStagedRendering)
        {
            return await ExecuteStagedPlanAsync(plan, cancellationToken);
        }

        var execution = await RunFfmpegAsync(plan.Arguments, plan.StderrPath, cancellationToken);
        var commandErrors = new List<string>();
        if (execution.ExitCode != 0) commandErrors.Add($"FFmpeg exited with code {execution.ExitCode}.");
        if (!File.Exists(plan.OutputPath)) commandErrors.Add($"Expected output was not created: {plan.OutputPath}");
        else if (new FileInfo(plan.OutputPath).Length <= 0) commandErrors.Add($"Expected output was empty: {plan.OutputPath}");
        var report = new WeeklyExistingRunFfmpegCommandReport(plan.EpisodeType, plan.OutputPath, true, true, false, execution.ExitCode, execution.ElapsedMilliseconds, plan.Command, Truncate(execution.StandardError, 12000), [], commandErrors);
        var output = File.Exists(plan.OutputPath) ? new FileInfo(plan.OutputPath) : null;
        var duration = LoadTimelineFromPlan(plan)?.ActualDurationSeconds ?? 0;
        return (report, (plan.EpisodeType, new WeeklyExistingRunEpisodeRenderReport(true, commandErrors.Count == 0, false, plan.OutputPath, duration, output?.Length ?? 0, plan.AudioAttached)));
    }

    private async Task<(WeeklyExistingRunFfmpegCommandReport Report, (string EpisodeType, WeeklyExistingRunEpisodeRenderReport RenderReport) Result)> ExecuteStagedPlanAsync(WeeklyExistingRunFfmpegCommandPlan plan, CancellationToken cancellationToken)
    {
        var timeline = LoadTimelineFromPlan(plan) ?? throw new InvalidOperationException($"Staged render timeline was not found for {plan.EpisodeType}.");
        Directory.CreateDirectory(plan.ClipsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputPath) ?? ".");
        var concatInputPath = GetConcatInputPath(plan.TempDirectory);
        var stderr = new StringBuilder();
        var errors = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var clipIndex = 0;
        int? lastExitCode = null;
        var debugFontPath = ResolveDebugFontPath();

        await File.WriteAllTextAsync(plan.StderrPath, string.Empty, cancellationToken);
        foreach (var shot in timeline.Segments.SelectMany(segment => segment.Shots))
        {
            clipIndex++;
            var clipPath = clipIndex <= plan.SegmentFiles.Count ? plan.SegmentFiles[clipIndex - 1] : Path.Combine(plan.ClipsDirectory, $"{clipIndex:0000}.mp4");
            var clipArgs = BuildStagedClipArguments(shot, clipPath, ResolveWidth(timeline, plan), ResolveHeight(timeline, plan), ResolveFps(timeline, plan), plan.DebugStoryboard, debugFontPath).ToList();
            var clipCommand = BuildCommandString(_renderingOptions.FfmpegPath, clipArgs);
            await AppendAllTextAsync(plan.StderrPath, $"\n\n===== {plan.EpisodeType} clip {clipIndex:0000} =====\n{clipCommand}\n", cancellationToken);
            var execution = await RunFfmpegAsync(clipArgs, plan.StderrPath, cancellationToken);
            lastExitCode = execution.ExitCode;
            stderr.AppendLine(execution.StandardError);
            if ((execution.ExitCode != 0 || !File.Exists(clipPath) || new FileInfo(clipPath).Length <= 0) && plan.DebugStoryboard && !string.IsNullOrWhiteSpace(debugFontPath))
            {
                await AppendAllTextAsync(plan.StderrPath, $"\nRetrying {plan.EpisodeType} clip {clipIndex:0000} without debug drawtext overlay.\n", cancellationToken);
                var fallbackArgs = BuildStagedClipArguments(shot, clipPath, ResolveWidth(timeline, plan), ResolveHeight(timeline, plan), ResolveFps(timeline, plan), false, null).ToList();
                execution = await RunFfmpegAsync(fallbackArgs, plan.StderrPath, cancellationToken);
                lastExitCode = execution.ExitCode;
                stderr.AppendLine(execution.StandardError);
            }
            if (execution.ExitCode != 0)
            {
                errors.Add($"FFmpeg exited with code {execution.ExitCode} while rendering clip {clipIndex}.");
                break;
            }
            if (!File.Exists(clipPath) || new FileInfo(clipPath).Length <= 0)
            {
                errors.Add($"Staged clip {clipIndex} was not created or was empty: {clipPath}");
                break;
            }
        }

        if (errors.Count == 0)
        {
            var concatLines = plan.SegmentFiles.Select(path => $"file '{EscapeConcatPath(path)}'");
            await File.WriteAllLinesAsync(concatInputPath, concatLines, cancellationToken);
            if (!File.Exists(concatInputPath)) errors.Add($"Concat input was not created: {concatInputPath}");
        }

        int? finalExitCode = null;
        if (errors.Count == 0)
        {
            await AppendAllTextAsync(plan.StderrPath, $"\n\n===== {plan.EpisodeType} concat =====\n{plan.Command}\n", cancellationToken);
            var finalExecution = await RunFfmpegAsync(plan.Arguments, plan.StderrPath, cancellationToken);
            finalExitCode = finalExecution.ExitCode;
            lastExitCode = finalExecution.ExitCode;
            stderr.AppendLine(finalExecution.StandardError);
            if (finalExecution.ExitCode != 0) errors.Add($"FFmpeg exited with code {finalExecution.ExitCode} while concatenating staged clips.");
            if (!File.Exists(plan.OutputPath) || new FileInfo(plan.OutputPath).Length <= 0) errors.Add($"Expected staged output was not created or was empty: {plan.OutputPath}");
        }

        stopwatch.Stop();
        var exitCode = errors.Count == 0 ? finalExitCode ?? 0 : lastExitCode;
        var report = new WeeklyExistingRunFfmpegCommandReport(plan.EpisodeType, plan.OutputPath, true, true, false, exitCode, stopwatch.ElapsedMilliseconds, plan.Command, Truncate(stderr.ToString(), 12000), ["Staged rendering used simplified fade/cut transitions."], errors);
        var output = File.Exists(plan.OutputPath) ? new FileInfo(plan.OutputPath) : null;
        return (report, (plan.EpisodeType, new WeeklyExistingRunEpisodeRenderReport(true, errors.Count == 0, false, plan.OutputPath, timeline.ActualDurationSeconds, output?.Length ?? 0, plan.AudioAttached)));
    }

    private static string? BuildFailedStage(WeeklyExistingRunFfmpegCommandReport? report)
    {
        if (report is null || report.Errors.Count == 0) return null;
        var first = report.Errors.First();
        if (first.Contains("clip", StringComparison.OrdinalIgnoreCase)) return $"{report.EpisodeType}:clip";
        if (first.Contains("concat", StringComparison.OrdinalIgnoreCase)) return $"{report.EpisodeType}:concat";
        return report.EpisodeType;
    }

    private static int? ExtractFailedShotNumber(WeeklyExistingRunFfmpegCommandReport? report)
    {
        var text = report?.Errors.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(text)) return null;
        var marker = "clip ";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = index + marker.Length;
        var digits = new string(text.Skip(start).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : null;
    }

    private async Task<(int ExitCode, long ElapsedMilliseconds, string StandardError)> RunFfmpegAsync(IReadOnlyList<string> arguments, string stderrPath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var processStart = new ProcessStartInfo { FileName = _renderingOptions.FfmpegPath, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) processStart.ArgumentList.Add(argument);
        using var process = Process.Start(processStart) ?? throw new InvalidOperationException("Failed to start FFmpeg process.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _renderingOptions.FfmpegTimeoutSeconds)));
        await process.WaitForExitAsync(timeoutCts.Token);
        await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();
        await AppendAllTextAsync(stderrPath, stderr, cancellationToken);
        return (process.ExitCode, stopwatch.ElapsedMilliseconds, stderr);
    }

    private static IEnumerable<string> BuildStagedClipArguments(FinalRenderShot shot, string clipPath, int width, int height, int fps, bool debugStoryboard, string? debugFontPath)
    {
        yield return "-y";
        yield return "-loop";
        yield return "1";
        yield return "-t";
        yield return Math.Max(1, shot.DurationSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return "-i";
        yield return shot.AssetPath;
        yield return "-vf";
        yield return BuildStagedClipFilter(shot, width, height, fps, debugStoryboard, debugFontPath);
        yield return "-r";
        yield return fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return "-an";
        yield return "-c:v";
        yield return "libx264";
        yield return "-preset";
        yield return "veryfast";
        yield return "-crf";
        yield return "20";
        yield return "-pix_fmt";
        yield return "yuv420p";
        yield return "-movflags";
        yield return "+faststart";
        yield return clipPath;
    }

    private static string BuildStagedClipFilter(FinalRenderShot shot, int width, int height, int fps, bool debugStoryboard, string? debugFontPath)
    {
        var frames = Math.Max(1, (int)Math.Ceiling(shot.DurationSeconds * fps));
        var fade = BuildShotFadeFilters(shot, fps);
        var overlay = debugStoryboard && !string.IsNullOrWhiteSpace(debugFontPath) ? BuildDebugStoryboardOverlay(shot, shot.ShotNumber - 1, debugFontPath, width, height) : string.Empty;
        if (width == ShortformCanvasWidth && height == ShortformCanvasHeight)
        {
            return $"{BuildShortformVerticalFilter(shot, frames, fps)}trim=duration={Math.Max(1, shot.DurationSeconds)},setpts=PTS-STARTPTS{fade}{overlay},format=yuv420p";
        }

        var zoom = BuildZoomExpression(shot.MotionEffect);
        var pan = BuildPanExpression(shot.MotionEffect);
        return $"scale={width * 2}:{height * 2}:force_original_aspect_ratio=increase,crop={width * 2}:{height * 2},zoompan=z='{zoom}':x='{pan.X}':y='{pan.Y}':d={frames}:s={width}x{height}:fps={fps},trim=duration={Math.Max(1, shot.DurationSeconds)},setpts=PTS-STARTPTS{fade}{overlay},format=yuv420p";
    }

    private static string BuildShortformVerticalFilter(FinalRenderShot shot, int frames, int fps)
    {
        return ResolveShortformLayoutMode(shot) switch
        {
            "ContainWithBackground" or "ContainBlurBackground" => BuildShortformContainFilter(fps),
            _ => BuildShortformCropFilter(shot, frames, fps)
        };
    }

    private static string BuildShortformCropFilter(FinalRenderShot shot, int frames, int fps)
    {
        var zoom = BuildZoomExpression(shot.MotionEffect);
        var pan = BuildPanExpression(shot.MotionEffect);
        return $"scale={ShortformCanvasWidth * 2}:{ShortformCanvasHeight * 2}:force_original_aspect_ratio=increase,crop={ShortformCanvasWidth * 2}:{ShortformCanvasHeight * 2},zoompan=z='{zoom}':x='{pan.X}':y='{pan.Y}':d={frames}:s={ShortformCanvasWidth}x{ShortformCanvasHeight}:fps={fps},";
    }

    private static string BuildShortformContainFilter(int fps)
        => $"split=2[bgsrc][fgsrc];[bgsrc]scale={ShortformCanvasWidth}:{ShortformCanvasHeight}:force_original_aspect_ratio=increase,crop={ShortformCanvasWidth}:{ShortformCanvasHeight},boxblur=24:2,eq=brightness=-0.10:saturation=0.75[bg];[fgsrc]scale={ShortformSafeContentWidth}:{ShortformSafeContentHeight}:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=({ShortformCanvasWidth}-w)/2:{ShortformSafeMarginTop}+({ShortformSafeContentHeight}-h)/2,fps={fps},";

    private static int ResolveWidth(FinalRenderEpisodeTimeline timeline, WeeklyExistingRunFfmpegCommandPlan plan)
        => plan.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase) ? 1080 : 1920;

    private static int ResolveHeight(FinalRenderEpisodeTimeline timeline, WeeklyExistingRunFfmpegCommandPlan plan)
        => plan.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase) ? 1920 : 1080;

    private static int ResolveFps(FinalRenderEpisodeTimeline timeline, WeeklyExistingRunFfmpegCommandPlan plan) => 30;

    private static string GetConcatInputPath(string tempDirectory) => Path.Combine(tempDirectory, "concat-input.txt");

    private static string EscapeConcatPath(string path) => path.Replace("'", "'\\''", StringComparison.Ordinal);

    private static int CountRenderedClips(WeeklyExistingRunFfmpegCommandPlan? plan)
        => plan is null ? 0 : plan.SegmentFiles.Count(path => File.Exists(path) && new FileInfo(path).Length > 0);

    private static async Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await File.AppendAllTextAsync(path, contents, cancellationToken);
    }

    private static string? ResolveDebugFontPath()
    {
        foreach (var candidate in new[]
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
            "/Library/Fonts/Arial.ttf",
            "C:/Windows/Fonts/arial.ttf"
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static RenderStoryboardReport BuildStoryboardReport(Guid pipelineRunId, IReadOnlyList<WeeklyExistingRunFfmpegCommandPlan> plans, WeeklyAudioAlignmentPlan audioPlan)
        => new(pipelineRunId, DateTime.UtcNow, plans.SelectMany(plan => (LoadTimelineFromPlan(plan)?.Segments ?? []).Select(segment => new RenderStoryboardSegmentReport(
            plan.EpisodeType,
            segment.SegmentType,
            segment.StartSecond,
            segment.EndSecond,
            Truncate((audioPlan.Segments.FirstOrDefault(a => a.EpisodeType.Equals(plan.EpisodeType, StringComparison.OrdinalIgnoreCase) && a.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase))?.NarrationText ?? string.Empty).ReplaceLineEndings(" "), 180),
            (segment.Shots ?? []).Select(shot => new RenderStoryboardShotReport(shot.ShotNumber, shot.AssetType, ResolveAssetCode(shot.AssetPath, shot.AssetId), ResolveSceneFamily(shot.AssetPath, shot.AssetId), shot.DurationSeconds, shot.Purpose)).ToList()))).ToList());

    private static FinalRenderEpisodeTimeline? LoadTimelineFromPlan(WeeklyExistingRunFfmpegCommandPlan plan)
        => File.Exists(plan.ConcatFilePath) ? JsonSerializer.Deserialize<FinalRenderEpisodeTimeline>(File.ReadAllText(plan.ConcatFilePath), JsonOptions) : null;

    private static string BuildDebugStoryboardOverlay(FinalRenderShot shot, int index, string fontPath, int width = 1920, int height = 1080)
    {
        var maxCharacters = width == ShortformCanvasWidth && height == ShortformCanvasHeight ? 84 : 140;
        var fontSize = width == ShortformCanvasWidth && height == ShortformCanvasHeight ? 22 : 28;
        var stripHeight = width == ShortformCanvasWidth && height == ShortformCanvasHeight ? Math.Min(ShortformSafeMarginTop - 48, 144) : 96;
        var y = width == ShortformCanvasWidth && height == ShortformCanvasHeight ? 32 : 28;
        var text = $"{shot.Purpose.Replace("render-refined primary visual for ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("render-refined supporting visual variety for ", string.Empty, StringComparison.OrdinalIgnoreCase)} | Shot {shot.ShotNumber} | {shot.AssetType} | {ResolveSceneFamily(shot.AssetPath, shot.AssetId)}/{ResolveAssetCode(shot.AssetPath, shot.AssetId)}";
        text = Truncate(text.ReplaceLineEndings(" "), maxCharacters);
        text = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal);
        var escapedFontPath = fontPath.Replace("\\", "/", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
        return $",drawbox=x=0:y=0:w=iw:h={stripHeight}:color=black@0.78:t=fill,drawtext=fontfile='{escapedFontPath}':text='{text}':x=28:y={y}:fontcolor=white:fontsize={fontSize}:box=0";
    }

    private static string ResolveAssetCode(string path, string assetId)
    {
        var haystack = path + " " + assetId;
        foreach (var key in new[] { "fast_cinematic_sky_hook", "cinematic_weekly_sky_reveal", "cosmic_retention_reset", "cosmic_closing_background", "shortform_call_to_action_background", "weekly-overview-timeline", "best-observation-window-card", "best-time-card", "where-to-look-card", "hero-event-card", "weekly-summary-card", "call-to-action-card", "visibility-calendar", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide", "01_establishing_wide", "03_hero_closeup" })
            if (haystack.Contains(key, StringComparison.OrdinalIgnoreCase)) return key;
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string ResolveSceneFamily(string path, string assetId)
    {
        var haystack = path + " " + assetId;
        if (haystack.Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase)) return "western_planet_grouping_scene";
        if (haystack.Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase)) return "moon_hero_scene";
        if (haystack.Contains("astrophotography_target_scene", StringComparison.OrdinalIgnoreCase)) return "expanded/astrophotography_target_scene";
        if (haystack.Contains("ai-cinematic", StringComparison.OrdinalIgnoreCase)) return "ai-cinematic";
        if (haystack.Contains("motion-graphics", StringComparison.OrdinalIgnoreCase)) return "motion-graphics";
        if (haystack.Contains("educational-overlays", StringComparison.OrdinalIgnoreCase)) return "educational-overlays";
        if (haystack.Contains("jwst", StringComparison.OrdinalIgnoreCase)) return "jwst";
        if (haystack.Contains("nasa", StringComparison.OrdinalIgnoreCase)) return "nasa";
        return "context";
    }

    private static string ClassifyAssetTypeFromPath(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (rel.Contains("ai-cinematic/", StringComparison.OrdinalIgnoreCase)) return "AICinematic";
        if (rel.Contains("assets/nasa/", StringComparison.OrdinalIgnoreCase)) return "NASA";
        if (rel.Contains("assets/jwst/", StringComparison.OrdinalIgnoreCase)) return "JWST";
        if (rel.Contains("assets/motion-graphics/", StringComparison.OrdinalIgnoreCase)) return "MotionGraphics";
        if (rel.Contains("assets/educational-overlays/", StringComparison.OrdinalIgnoreCase)) return "EducationalOverlay";
        if (rel.Contains("astrophotography_target_scene", StringComparison.OrdinalIgnoreCase)) return "StellariumExpanded";
        if (rel.Contains("stellarium/scenes/", StringComparison.OrdinalIgnoreCase)) return "StellariumBase";
        return "Image";
    }

    private static bool IsSupportedImagePath(string path) => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeAssetId(string sourceType, string path, string assetCode) => $"{NormalizeRenderAssetType(sourceType)}:{ResolveSceneFamily(path, assetCode)}:{ResolveAssetCode(path, assetCode)}";

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
            File.Exists(paths.FinalShotList) ? await ReadJsonAsync<IReadOnlyList<FinalRenderShotListEntry>>(paths.FinalShotList, cancellationToken) : [],
            File.Exists(paths.ResolvedRenderShotPlan) ? await ReadJsonAsync<ResolvedRenderShotPlan>(paths.ResolvedRenderShotPlan, cancellationToken) : null,
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
        if (request.UseAudioDrivenTimeline)
        {
            var renderRoot = Path.Combine(root, "render");
            var audioDrivenShotPlanPath = Path.Combine(renderRoot, "audio-driven-resolved-render-shot-plan.json");
            var audioDrivenContractPath = Path.Combine(renderRoot, "audio-driven-render-contract.json");
            var audioDrivenTimelinePath = Path.Combine(renderRoot, "audio-driven-final-render-timeline.json");
            if (!File.Exists(audioDrivenShotPlanPath)) errors.Add($"Audio-driven shot plan is missing: {audioDrivenShotPlanPath}");
            if (!File.Exists(audioDrivenContractPath)) errors.Add($"Audio-driven render contract is missing: {audioDrivenContractPath}");
            if (!File.Exists(audioDrivenTimelinePath)) errors.Add($"Audio-driven final render timeline is missing: {audioDrivenTimelinePath}");
        }
        if (request.MergeAudio || !request.AllowSilent)
        {
            if (request.RenderLongform && !File.Exists(Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3"))) errors.Add($"Longform narration audio is missing: {Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3")}");
            if (request.RenderShortform && !File.Exists(Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3"))) errors.Add($"Shortform narration audio is missing: {Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3")}");
        }

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
    ResolvedRenderShotPlan? ResolvedShotPlan,
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
    string ResolvedRenderShotPlan,
    string ProductionAssetManifest,
    bool UseAudioDrivenTimeline)
{
    public IReadOnlyList<string> All => UseAudioDrivenTimeline
        ? [RenderContract, InputManifest, FilterGraphPlan, TransitionPlan, MotionPlan, AudioPlan, FinalTimeline, ResolvedRenderShotPlan]
        : [RenderContract, InputManifest, FilterGraphPlan, TransitionPlan, MotionPlan, AudioPlan, FinalTimeline, FinalShotList];

    public static WeeklyExistingRunRequiredPaths FromRoot(string root, bool useAudioDrivenTimeline = false)
        => new(
            Path.Combine(root, "render", useAudioDrivenTimeline ? "audio-driven-render-contract.json" : "weekly-render-contract.json"),
            Path.Combine(root, "render", "render-input-manifest.json"),
            Path.Combine(root, "render", "ffmpeg-filtergraph-plan.json"),
            Path.Combine(root, "render", "transition-execution-plan.json"),
            Path.Combine(root, "render", "motion-effect-execution-plan.json"),
            Path.Combine(root, "render", "audio-alignment-plan.json"),
            useAudioDrivenTimeline ? Path.Combine(root, "render", "audio-driven-final-render-timeline.json") : Path.Combine(root, "episode", "final-render-timeline.json"),
            Path.Combine(root, "episode", "final-render-shot-list.json"),
            useAudioDrivenTimeline ? Path.Combine(root, "render", "audio-driven-resolved-render-shot-plan.json") : Path.Combine(root, "render", "resolved-render-shot-plan.json"),
            Path.Combine(root, "episode", "weekly-production-asset-manifest.json"),
            useAudioDrivenTimeline);
}

internal static class WeeklyExistingRunEpisodeRenderReportFactory
{
    public static WeeklyExistingRunEpisodeRenderReport NotRequested(string outputPath) => new(false, false, false, outputPath, 0, 0, false);
}
