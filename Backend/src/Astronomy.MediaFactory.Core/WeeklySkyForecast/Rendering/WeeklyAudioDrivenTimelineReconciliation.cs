using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;

public interface IWeeklyAudioDrivenTimelineReconciliationService
{
    Task<WeeklyAudioDrivenTimelineReconciliationResponse> ReconcileAsync(Guid pipelineRunId, WeeklyAudioDrivenTimelineReconciliationRequest request, CancellationToken cancellationToken);
}

public sealed record WeeklyAudioDrivenTimelineReconciliationRequest(
    bool ReconcileLongform = true,
    bool ReconcileShortform = true,
    bool OverwriteExisting = true,
    bool DryRun = false);

public sealed record WeeklyAudioDrivenTimelineReconciliationResponse(
    Guid PipelineRunId,
    bool AudioDrivenTimelineReady,
    bool LongformAudioDrivenTimelineReady,
    bool ShortformAudioDrivenTimelineReady,
    double OldLongformDurationSeconds,
    double NewLongformDurationSeconds,
    double OldShortformDurationSeconds,
    double NewShortformDurationSeconds,
    string AudioDrivenFinalRenderTimelinePath,
    string AudioDrivenResolvedRenderShotPlanPath,
    string AudioDrivenRenderContractPath,
    string AudioDrivenStoryboardReportPath,
    string AudioDrivenTimelineReconciliationReportPath,
    string AudioDrivenTimelineValidationReportPath,
    string AudioDrivenReconciliationInputResolutionReportPath,
    string InputMode,
    bool AudioDrivenTimelineValid,
    bool LongformSegmentTimingValid,
    bool LongformShotTimingValid,
    bool LongformTimelineContinuous,
    int LongformInvalidShotCount,
    int LongformGapCount,
    int LongformOverlapCount,
    bool ShortformSegmentTimingValid,
    bool ShortformShotTimingValid,
    bool ShortformTimelineContinuous,
    int ShortformInvalidShotCount,
    int ShortformGapCount,
    int ShortformOverlapCount,
    string ResolvedPipelineRunRoot,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyAudioDrivenTimelineReconciliationReport(
    Guid PipelineRunId,
    bool AudioDrivenTimelineReady,
    string InputMode,
    bool AudioDrivenTimelineValid,
    WeeklyAudioDrivenEpisodeReconciliationReport Longform,
    WeeklyAudioDrivenEpisodeReconciliationReport Shortform,
    bool VideoDurationNowMatchesAudio,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyAudioDrivenEpisodeReconciliationReport(
    double OldDurationSeconds,
    double NewDurationSeconds,
    int SegmentCount,
    int SegmentsReconciled);

public sealed record WeeklyAudioDrivenReconciliationInputResolutionReport(
    bool InputResolutionReady,
    string InputMode,
    bool NewContractFilesFound,
    bool LegacyFilesFound,
    bool FinalRenderTimelineFound,
    bool FinalRenderShotListFound,
    bool WeeklyRenderContractFound,
    bool RenderInputManifestFound,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyAudioDrivenTimelineValidationReport(
    Guid PipelineRunId,
    bool AudioDrivenTimelineValid,
    WeeklyAudioDrivenEpisodeTimelineValidationReport Longform,
    WeeklyAudioDrivenEpisodeTimelineValidationReport Shortform,
    bool DynamicGroupingPreservationReady,
    string HeroGroupingParentSceneCode,
    IReadOnlyList<string> HeroGroupingChildSceneCodes,
    int HeroGroupingPreservedFrameCount,
    int ShortformGroupingPreservedShotCount,
    bool ShortformCtaVisualPreserved,
    IReadOnlyList<string> PreservationValidationErrors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyAudioDrivenDynamicGroupingPreservationReport(
    bool DynamicGroupingPreservationReady,
    string HeroGroupingParentSceneCode,
    IReadOnlyList<string> HeroGroupingChildSceneCodes,
    int HeroGroupingPreservedFrameCount,
    int ShortformGroupingPreservedShotCount,
    bool ShortformCtaVisualPreserved,
    IReadOnlyList<string> PreservationValidationErrors);

public sealed record WeeklyAudioDrivenEpisodeTimelineValidationReport(
    bool SegmentTimingValid,
    bool ShotTimingValid,
    bool TimelineContinuous,
    double EpisodeDurationSeconds,
    int InvalidSegmentCount,
    int InvalidShotCount,
    int GapCount,
    int OverlapCount);

public sealed class WeeklyAudioDrivenTimelineReconciliationService(
    IWeeklyPipelineRunDirectoryResolver pipelineRunDirectoryResolver,
    ILogger<WeeklyAudioDrivenTimelineReconciliationService> logger) : IWeeklyAudioDrivenTimelineReconciliationService
{
    private const double ToleranceSeconds = 0.02d;
    private const double AudioDurationToleranceSeconds = 0.05d;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public async Task<WeeklyAudioDrivenTimelineReconciliationResponse> ReconcileAsync(Guid pipelineRunId, WeeklyAudioDrivenTimelineReconciliationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("WEEKLY_AUDIO_DRIVEN_TIMELINE_RECONCILE_START pipelineRunId={PipelineRunId} dryRun={DryRun} longform={Longform} shortform={Shortform}", pipelineRunId, request.DryRun, request.ReconcileLongform, request.ReconcileShortform);

        var warnings = new List<string>();
        var errors = new List<string>();
        var root = await pipelineRunDirectoryResolver.ResolveRunDirectoryAsync(pipelineRunId);
        var paths = WeeklyAudioDrivenTimelineReconciliationPaths.FromRoot(root);
        var inputResolution = ResolveInputContract(paths);
        warnings.AddRange(inputResolution.Warnings);
        errors.AddRange(inputResolution.Errors);
        if (errors.Count > 0)
        {
            if (!request.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(paths.AudioDrivenReconciliationInputResolutionReport)!);
                await WriteJsonAsync(paths.AudioDrivenReconciliationInputResolutionReport, inputResolution, cancellationToken);
            }

            throw new FileNotFoundException(string.Join(" ", errors));
        }

        var manifest = await ReadJsonAsync<WeeklyAudioSegmentManifest>(paths.AudioSegmentManifest, cancellationToken);
        _ = await ReadJsonAsync<WeeklyAudioTimingValidationReport>(paths.AudioTimingValidationReport, cancellationToken);
        var sourceTimeline = await ReadJsonAsync<FinalRenderTimeline>(paths.FinalRenderTimeline, cancellationToken);
        if (inputResolution.InputMode.Equals("NewRendererContract", StringComparison.OrdinalIgnoreCase))
        {
            _ = await ReadJsonAsync<IReadOnlyList<FinalRenderShotListEntry>>(paths.FinalRenderShotList, cancellationToken);
            _ = await ReadJsonAsync<WeeklyRenderInputManifest>(paths.RenderInputManifest, cancellationToken);
        }

        var sourceShotPlan = inputResolution.InputMode.Equals("LegacyResolvedShotPlan", StringComparison.OrdinalIgnoreCase)
            ? await ReadJsonAsync<ResolvedRenderShotPlan>(paths.ResolvedRenderShotPlan, cancellationToken)
            : new ResolvedRenderShotPlan(pipelineRunId, DateTime.UtcNow, [ToShotPlan("longform", sourceTimeline.Longform), ToShotPlan("shortform", sourceTimeline.Shortform)]);
        var sourceStoryboard = inputResolution.InputMode.Equals("LegacyResolvedShotPlan", StringComparison.OrdinalIgnoreCase)
            ? await ReadJsonAsync<RenderStoryboardReport>(paths.RenderStoryboardReport, cancellationToken)
            : null;
        var sourceContract = await ReadJsonAsync<WeeklyRenderContract>(paths.RenderContract, cancellationToken);

        if (manifest.PipelineRunId != pipelineRunId) errors.Add($"Audio segment manifest pipelineRunId {manifest.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (sourceTimeline.PipelineRunId != pipelineRunId) errors.Add($"Final render timeline pipelineRunId {sourceTimeline.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (sourceShotPlan.PipelineRunId != pipelineRunId) errors.Add($"Resolved render shot plan pipelineRunId {sourceShotPlan.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (sourceContract.PipelineRunId != pipelineRunId) errors.Add($"Render contract pipelineRunId {sourceContract.PipelineRunId} does not match requested pipelineRunId {pipelineRunId}.");
        if (!File.Exists(paths.LongformCombinedAudio)) errors.Add($"Required longform combined audio file is missing: {paths.LongformCombinedAudio}");
        if (!File.Exists(paths.ShortformCombinedAudio)) errors.Add($"Required shortform combined audio file is missing: {paths.ShortformCombinedAudio}");

        var longform = request.ReconcileLongform
            ? ReconcileEpisode("longform", sourceTimeline.Longform, FindEpisodePlan(sourceShotPlan, "longform"), manifest.Longform, 4d, 12d, warnings, errors)
            : new EpisodeReconciliationResult(sourceTimeline.Longform, FindEpisodePlan(sourceShotPlan, "longform") ?? ToShotPlan("longform", sourceTimeline.Longform), sourceTimeline.Longform.ActualDurationSeconds, 0);

        var shortform = request.ReconcileShortform
            ? ReconcileEpisode("shortform", sourceTimeline.Shortform, FindEpisodePlan(sourceShotPlan, "shortform"), manifest.Shortform, 2d, 8d, warnings, errors)
            : new EpisodeReconciliationResult(sourceTimeline.Shortform, FindEpisodePlan(sourceShotPlan, "shortform") ?? ToShotPlan("shortform", sourceTimeline.Shortform), sourceTimeline.Shortform.ActualDurationSeconds, 0);

        var reconciledTimeline = new FinalRenderTimeline(pipelineRunId, DateTime.UtcNow, longform.Timeline, shortform.Timeline);
        var reconciledShotPlan = new ResolvedRenderShotPlan(pipelineRunId, DateTime.UtcNow, [longform.ShotPlan, shortform.ShotPlan]);
        var reconciledContract = sourceContract with
        {
            Longform = sourceContract.Longform with { DurationSeconds = (int)Math.Ceiling(longform.NewDurationSeconds), TimelinePath = paths.AudioDrivenFinalRenderTimeline, ShotCount = longform.ShotPlan.Segments.Sum(s => s.Shots.Count) },
            Shortform = sourceContract.Shortform with { DurationSeconds = (int)Math.Ceiling(shortform.NewDurationSeconds), TimelinePath = paths.AudioDrivenFinalRenderTimeline, ShotCount = shortform.ShotPlan.Segments.Sum(s => s.Shots.Count) }
        };
        var reconciledStoryboard = BuildStoryboardReport(pipelineRunId, reconciledTimeline, sourceStoryboard);

        var preservationReport = ValidateMandatoryShots(reconciledShotPlan);
        errors.AddRange(preservationReport.PreservationValidationErrors);

        var validationReport = ValidateAudioDrivenTimeline(pipelineRunId, reconciledTimeline, request.ReconcileLongform ? manifest.Longform : [], request.ReconcileShortform ? manifest.Shortform : [], warnings, preservationReport);

        var allErrors = errors.Concat(validationReport.Errors).ToList();
        var longformAudioTotal = request.ReconcileLongform ? manifest.Longform.Sum(x => x.ActualAudioDurationSeconds) : longform.NewDurationSeconds;
        var shortformAudioTotal = request.ReconcileShortform ? manifest.Shortform.Sum(x => x.ActualAudioDurationSeconds) : shortform.NewDurationSeconds;
        var videoDurationNowMatchesAudio = Math.Abs(longform.NewDurationSeconds - longformAudioTotal) <= AudioDurationToleranceSeconds && Math.Abs(shortform.NewDurationSeconds - shortformAudioTotal) <= AudioDurationToleranceSeconds;
        if (!videoDurationNowMatchesAudio) allErrors.Add("Audio-driven video duration does not match audio manifest duration.");

        var ready = allErrors.Count == 0 && validationReport.AudioDrivenTimelineValid && videoDurationNowMatchesAudio;
        var report = new WeeklyAudioDrivenTimelineReconciliationReport(
            pipelineRunId,
            ready,
            inputResolution.InputMode,
            validationReport.AudioDrivenTimelineValid,
            new WeeklyAudioDrivenEpisodeReconciliationReport(sourceTimeline.Longform.ActualDurationSeconds, Round(longform.NewDurationSeconds), sourceTimeline.Longform.Segments.Count, longform.SegmentsReconciled),
            new WeeklyAudioDrivenEpisodeReconciliationReport(sourceTimeline.Shortform.ActualDurationSeconds, Round(shortform.NewDurationSeconds), sourceTimeline.Shortform.Segments.Count, shortform.SegmentsReconciled),
            videoDurationNowMatchesAudio,
            warnings,
            allErrors);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.AudioDrivenFinalRenderTimeline)!);
            EnsureOutputWritable(paths, request.OverwriteExisting);
            await WriteJsonAsync(paths.AudioDrivenFinalRenderTimeline, reconciledTimeline, cancellationToken);
            await WriteJsonAsync(paths.AudioDrivenResolvedRenderShotPlan, reconciledShotPlan, cancellationToken);
            await WriteJsonAsync(paths.AudioDrivenRenderContract, reconciledContract, cancellationToken);
            await WriteJsonAsync(paths.AudioDrivenStoryboardReport, reconciledStoryboard, cancellationToken);
            await WriteJsonAsync(paths.AudioDrivenTimelineReconciliationReport, report, cancellationToken);
            await WriteJsonAsync(paths.AudioDrivenTimelineValidationReport, validationReport, cancellationToken);
            await WriteJsonAsync(paths.AudioDrivenReconciliationInputResolutionReport, inputResolution, cancellationToken);
        }

        logger.LogInformation("WEEKLY_AUDIO_DRIVEN_TIMELINE_RECONCILE_COMPLETE pipelineRunId={PipelineRunId} ready={Ready}", pipelineRunId, ready);
        return new WeeklyAudioDrivenTimelineReconciliationResponse(
            pipelineRunId,
            ready,
            request.ReconcileLongform && allErrors.Count == 0,
            request.ReconcileShortform && allErrors.Count == 0,
            sourceTimeline.Longform.ActualDurationSeconds,
            Round(longform.NewDurationSeconds),
            sourceTimeline.Shortform.ActualDurationSeconds,
            Round(shortform.NewDurationSeconds),
            paths.AudioDrivenFinalRenderTimeline,
            paths.AudioDrivenResolvedRenderShotPlan,
            paths.AudioDrivenRenderContract,
            paths.AudioDrivenStoryboardReport,
            paths.AudioDrivenTimelineReconciliationReport,
            paths.AudioDrivenTimelineValidationReport,
            paths.AudioDrivenReconciliationInputResolutionReport,
            inputResolution.InputMode,
            validationReport.AudioDrivenTimelineValid,
            validationReport.Longform.SegmentTimingValid,
            validationReport.Longform.ShotTimingValid,
            validationReport.Longform.TimelineContinuous,
            validationReport.Longform.InvalidShotCount,
            validationReport.Longform.GapCount,
            validationReport.Longform.OverlapCount,
            validationReport.Shortform.SegmentTimingValid,
            validationReport.Shortform.ShotTimingValid,
            validationReport.Shortform.TimelineContinuous,
            validationReport.Shortform.InvalidShotCount,
            validationReport.Shortform.GapCount,
            validationReport.Shortform.OverlapCount,
            root,
            warnings,
            allErrors);
    }

    private EpisodeReconciliationResult ReconcileEpisode(string episodeType, FinalRenderEpisodeTimeline sourceTimeline, ResolvedRenderEpisodeShotPlan? sourceShotPlan, IReadOnlyList<WeeklyAudioSegmentManifestEntry> audioSegments, double minShotSeconds, double maxShotSeconds, List<string> warnings, List<string> errors)
    {
        var audioById = audioSegments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var shotPlanSegments = (sourceShotPlan?.Segments ?? []).ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var segments = new List<FinalRenderSegment>();
        var resolvedSegments = new List<ResolvedRenderSegmentShotPlan>();
        var cursor = 0d;
        var reconciled = 0;

        foreach (var sourceSegment in sourceTimeline.Segments)
        {
            double audioDuration;
            if (audioById.TryGetValue(sourceSegment.SegmentId, out var audio) && audio.ActualAudioDurationSeconds > 0)
            {
                audioDuration = audio.ActualAudioDurationSeconds;
                reconciled++;
            }
            else
            {
                audioDuration = sourceSegment.DurationSeconds;
                warnings.Add($"{episodeType} segment {sourceSegment.SegmentId} is missing actual audio duration; kept existing segment duration {sourceSegment.DurationSeconds}s.");
            }

            var planSegment = shotPlanSegments.TryGetValue(sourceSegment.SegmentId, out var resolvedPlan) ? resolvedPlan : ToShotPlanSegment(episodeType, sourceSegment);
            var start = Round(cursor);
            var end = Round(cursor + audioDuration);
            var duration = Round(end - start);
            var shots = AllocateShots(episodeType, sourceSegment, planSegment.Shots, cursor, audioDuration, start, end, minShotSeconds, maxShotSeconds, warnings, errors);
            segments.Add(sourceSegment with { StartSecond = start, EndSecond = end, DurationSeconds = duration, NarrationStart = start, NarrationEnd = end, Shots = shots });
            resolvedSegments.Add(new ResolvedRenderSegmentShotPlan(episodeType, sourceSegment.SegmentId, sourceSegment.SegmentType, start, end, duration, shots.Select(ToResolvedShot).ToList()));
            cursor += audioDuration;
        }

        return new EpisodeReconciliationResult(new FinalRenderEpisodeTimeline(sourceTimeline.TargetDurationSeconds, Round(cursor), segments), new ResolvedRenderEpisodeShotPlan(episodeType, Round(cursor), resolvedSegments), cursor, reconciled);
    }

    private static IReadOnlyList<FinalRenderShot> AllocateShots(string episodeType, FinalRenderSegment segment, IReadOnlyList<ResolvedRenderShotPlanEntry> sourceShots, double segmentStart, double segmentDuration, double roundedSegmentStart, double roundedSegmentEnd, double minShotSeconds, double maxShotSeconds, List<string> warnings, List<string> errors)
    {
        var selected = sourceShots.Select(ToFinalShot).ToList();
        if (selected.Count == 0) selected = segment.Shots.ToList();
        if (selected.Count == 0)
        {
            errors.Add($"{episodeType} segment {segment.SegmentId} has no shots to reconcile.");
            return [];
        }

        var mandatory = selected.Where(s => IsMandatoryShot(episodeType, segment.SegmentType, s)).ToList();
        var maxCount = Math.Max(mandatory.Count, Math.Max(1, (int)Math.Floor(segmentDuration / minShotSeconds)));
        if (selected.Count > maxCount)
        {
            selected = selected.Where(s => mandatory.Any(m => SameShot(m, s))).Concat(selected.Where(s => !mandatory.Any(m => SameShot(m, s))).Take(Math.Max(0, maxCount - mandatory.Count))).DistinctBy(s => s.AssetPath + "|" + s.AssetId + "|" + s.ShotNumber, StringComparer.OrdinalIgnoreCase).ToList();
            warnings.Add($"{episodeType} segment {segment.SegmentId} dropped supporting shots to satisfy audio duration pacing.");
        }

        while (selected.Count > 1 && segmentDuration / selected.Count < minShotSeconds && selected.Any(s => !mandatory.Any(m => SameShot(m, s))))
        {
            var drop = selected.Last(s => !mandatory.Any(m => SameShot(m, s)));
            selected.Remove(drop);
            warnings.Add($"{episodeType} segment {segment.SegmentId} dropped supporting shot {drop.ShotNumber} to keep shot duration above {minShotSeconds}s.");
        }

        while (segmentDuration / selected.Count > maxShotSeconds)
        {
            var repeat = selected.LastOrDefault(IsRepeatableShot);
            if (repeat is null)
            {
                warnings.Add($"{episodeType} segment {segment.SegmentId} has no repeatable cinematic/background shot available for audio-duration normalization.");
                break;
            }

            selected.Add(repeat with { ShotNumber = selected.Count + 1, Purpose = repeat.Purpose + " (audio-duration repeat)" });
            warnings.Add($"{episodeType} segment {segment.SegmentId} repeated cinematic/background asset to keep shot duration below {maxShotSeconds}s.");
        }

        var allocated = new List<FinalRenderShot>();
        var previousRoundedEnd = roundedSegmentStart;
        for (var i = 0; i < selected.Count; i++)
        {
            var end = i == selected.Count - 1 ? segmentStart + segmentDuration : segmentStart + (segmentDuration * (i + 1) / selected.Count);
            var roundedStart = i == 0 ? roundedSegmentStart : previousRoundedEnd;
            var roundedEnd = i == selected.Count - 1 ? roundedSegmentEnd : Round(end);
            var shot = selected[i];
            allocated.Add(shot with { ShotNumber = i + 1, StartSecond = roundedStart, EndSecond = roundedEnd, DurationSeconds = Round(roundedEnd - roundedStart) });
            previousRoundedEnd = roundedEnd;
        }
        return allocated;
    }

    private static bool IsMandatoryShot(string episodeType, string segmentType, FinalRenderShot shot)
    {
        var haystack = shot.AssetPath + " " + shot.AssetId + " " + shot.AssetType + " " + shot.Purpose;
        if (episodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(haystack, "hook", "fast_cinematic_sky_hook") || IsDynamicGroupingShot(haystack) || IsMoonHeroContextShot(haystack) || IsCtaVisual(haystack, segmentType);
        }
        return segmentType switch
        {
            "HeroEvent" or "StrongestEvent" => IsDynamicGroupingShot(haystack) || IsMoonHeroContextShot(haystack),
            "PlanetHighlights" => IsDynamicGroupingShot(haystack),
            "MoonHighlights" => IsMoonHeroContextShot(haystack),
            "AstrophotographyTip" => ContainsAny(haystack, "astrophotography_target_scene", "ExpandedStellarium"),
            "BestObservationWindow" => ContainsAny(haystack, "best-observation-window-card", "best-time-card"),
            "WeeklySummary" => ContainsAny(haystack, "closing", "weekly-summary-card", "cosmic_closing_background"),
            _ => false
        };
    }

    private static WeeklyAudioDrivenDynamicGroupingPreservationReport ValidateMandatoryShots(ResolvedRenderShotPlan shotPlan)
    {
        const string parentSceneCode = "western_planet_grouping_scene";
        var errors = new List<string>();
        var childSceneCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var heroGroupingPreservedFrameCount = 0;
        var shortformGroupingPreservedShotCount = 0;
        var shortformCtaVisualPreserved = false;

        foreach (var episode in shotPlan.Episodes)
        {
            if (episode.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase))
            {
                var shots = episode.Segments.SelectMany(s => s.Shots.Select(shot => (SegmentType: s.SegmentType, Shot: shot))).ToList();
                if (!shots.Any(s => ContainsAny(ShotText(s.Shot), "hook", "fast_cinematic_sky_hook"))) errors.Add("Shortform hook visual was not preserved.");
                shortformGroupingPreservedShotCount = shots.Count(s => IsShortformGroupingPreservationShot(ShotText(s.Shot), s.SegmentType));
                if (shortformGroupingPreservedShotCount < 2) errors.Add("Shortform must preserve at least 2 grouping shots.");
                shortformCtaVisualPreserved = shots.Any(s => IsCtaVisual(ShotText(s.Shot), s.SegmentType));
                if (!shortformCtaVisualPreserved) errors.Add("Shortform CTA visual was not preserved.");
                foreach (var shot in shots.Select(s => s.Shot)) AddGroupingChildSceneCode(ShotText(shot), childSceneCodes);
                continue;
            }

            foreach (var segment in episode.Segments)
            {
                var shots = segment.Shots;
                foreach (var shot in shots) AddGroupingChildSceneCode(ShotText(shot), childSceneCodes);
                if (segment.SegmentType is "HeroEvent" or "StrongestEvent")
                {
                    var parentGroupingCount = shots.Count(s => IsWesternGroupingParentShot(ShotText(s)));
                    var splitGroupingCount = shots.Count(s => IsDynamicGroupingShot(ShotText(s)));
                    var hasVenusGrouping = shots.Any(s => IsGroupingFocusShot(ShotText(s), "venus"));
                    var hasSaturnGrouping = shots.Any(s => IsGroupingFocusShot(ShotText(s), "saturn"));
                    var hasMoonOrHeroContext = shots.Any(s => IsMoonHeroContextShot(ShotText(s)) || ContainsAny(ShotText(s), "hero-event-card", "hero_event_card", "HeroEvent"));
                    heroGroupingPreservedFrameCount = Math.Max(heroGroupingPreservedFrameCount, splitGroupingCount);
                    if (parentGroupingCount < 3 && splitGroupingCount < 3 && !(hasVenusGrouping && hasSaturnGrouping && hasMoonOrHeroContext))
                    {
                        errors.Add("HeroEvent must preserve at least 3 western_planet_grouping_scene frames.");
                    }
                }
                if (segment.SegmentType.Equals("PlanetHighlights", StringComparison.OrdinalIgnoreCase) && shots.Count(s => IsDynamicGroupingShot(ShotText(s))) < 2) errors.Add("PlanetHighlights must preserve at least 2 western_planet_grouping_scene frames.");
                if (segment.SegmentType.Equals("MoonHighlights", StringComparison.OrdinalIgnoreCase) && shots.Count(s => IsMoonHeroContextShot(ShotText(s))) < 2) errors.Add("MoonHighlights must preserve at least 2 moon_hero_scene frames.");
                if (segment.SegmentType.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase) && !shots.Any(s => ContainsAny(ShotText(s), "astrophotography_target_scene", "ExpandedStellarium"))) errors.Add("AstrophotographyTip must preserve an ExpandedStellarium frame.");
                if (segment.SegmentType.Equals("BestObservationWindow", StringComparison.OrdinalIgnoreCase) && !shots.Any(s => ContainsAny(ShotText(s), "best-observation-window-card", "best-time-card"))) errors.Add("BestObservationWindow must preserve a best-observation-window-card or best-time-card visual.");
                if (segment.SegmentType.Equals("WeeklySummary", StringComparison.OrdinalIgnoreCase) && !shots.Any(s => ContainsAny(ShotText(s), "closing", "weekly-summary-card", "cosmic_closing_background"))) errors.Add("WeeklySummary must preserve closing cinematic or weekly-summary-card visual.");
            }
        }

        return new WeeklyAudioDrivenDynamicGroupingPreservationReport(errors.Count == 0, parentSceneCode, childSceneCodes.ToList(), heroGroupingPreservedFrameCount, shortformGroupingPreservedShotCount, shortformCtaVisualPreserved, errors);
    }

    private static WeeklyAudioDrivenTimelineValidationReport ValidateAudioDrivenTimeline(Guid pipelineRunId, FinalRenderTimeline timeline, IReadOnlyList<WeeklyAudioSegmentManifestEntry> longformAudioSegments, IReadOnlyList<WeeklyAudioSegmentManifestEntry> shortformAudioSegments, IReadOnlyList<string> warnings, WeeklyAudioDrivenDynamicGroupingPreservationReport preservationReport)
    {
        var errors = new List<string>();
        var longform = ValidateAudioDrivenEpisodeTimeline("longform", timeline.Longform, longformAudioSegments, errors);
        var shortform = ValidateAudioDrivenEpisodeTimeline("shortform", timeline.Shortform, shortformAudioSegments, errors);

        return new WeeklyAudioDrivenTimelineValidationReport(
            pipelineRunId,
            longform.SegmentTimingValid && longform.ShotTimingValid && longform.TimelineContinuous && shortform.SegmentTimingValid && shortform.ShotTimingValid && shortform.TimelineContinuous,
            longform,
            shortform,
            preservationReport.DynamicGroupingPreservationReady,
            preservationReport.HeroGroupingParentSceneCode,
            preservationReport.HeroGroupingChildSceneCodes,
            preservationReport.HeroGroupingPreservedFrameCount,
            preservationReport.ShortformGroupingPreservedShotCount,
            preservationReport.ShortformCtaVisualPreserved,
            preservationReport.PreservationValidationErrors,
            warnings.ToList(),
            errors);
    }

    private static WeeklyAudioDrivenEpisodeTimelineValidationReport ValidateAudioDrivenEpisodeTimeline(string episodeType, FinalRenderEpisodeTimeline timeline, IReadOnlyList<WeeklyAudioSegmentManifestEntry> audioSegments, List<string> errors)
    {
        var invalidSegmentCount = 0;
        var invalidShotCount = 0;
        var gapCount = 0;
        var overlapCount = 0;
        var audioById = audioSegments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        double? previousSegmentEnd = null;

        for (var segmentIndex = 0; segmentIndex < timeline.Segments.Count; segmentIndex++)
        {
            var segment = timeline.Segments[segmentIndex];
            var segmentInvalid = false;

            if (segment.EndSecond <= segment.StartSecond || Math.Abs((segment.EndSecond - segment.StartSecond) - segment.DurationSeconds) > ToleranceSeconds)
            {
                segmentInvalid = true;
                errors.Add($"{episodeType} segment {segment.SegmentId} timing is invalid: start={segment.StartSecond:0.###} end={segment.EndSecond:0.###} duration={segment.DurationSeconds:0.###}.");
            }

            if (audioById.TryGetValue(segment.SegmentId, out var audio) && Math.Abs(segment.DurationSeconds - audio.ActualAudioDurationSeconds) > AudioDurationToleranceSeconds)
            {
                segmentInvalid = true;
                errors.Add($"{episodeType} segment {segment.SegmentId} duration {segment.DurationSeconds:0.###}s does not match actual audio duration {audio.ActualAudioDurationSeconds:0.###}s.");
            }

            if (segmentIndex == 0 && Math.Abs(segment.StartSecond) > ToleranceSeconds)
            {
                gapCount++;
                errors.Add($"{episodeType} first segment starts at {segment.StartSecond:0.###} instead of 0.");
            }

            if (previousSegmentEnd is not null)
            {
                var delta = segment.StartSecond - previousSegmentEnd.Value;
                if (delta > ToleranceSeconds)
                {
                    gapCount++;
                    errors.Add($"{episodeType} timeline has a segment gap of {delta:0.###}s before {segment.SegmentId}.");
                }
                else if (delta < -ToleranceSeconds)
                {
                    overlapCount++;
                    errors.Add($"{episodeType} timeline has a segment overlap of {Math.Abs(delta):0.###}s at {segment.SegmentId}.");
                }
            }

            if (segmentInvalid) invalidSegmentCount++;
            previousSegmentEnd = segment.EndSecond;

            if (segment.Shots.Count == 0)
            {
                invalidShotCount++;
                errors.Add($"{episodeType} segment {segment.SegmentId} has no shots.");
                continue;
            }

            for (var shotIndex = 0; shotIndex < segment.Shots.Count; shotIndex++)
            {
                var shot = segment.Shots[shotIndex];
                var shotInvalid = false;
                if (shot.EndSecond <= shot.StartSecond || Math.Abs((shot.EndSecond - shot.StartSecond) - shot.DurationSeconds) > ToleranceSeconds)
                {
                    shotInvalid = true;
                    errors.Add($"{episodeType} segment {segment.SegmentId} shot {shot.ShotNumber} duration is invalid: start={shot.StartSecond:0.###} end={shot.EndSecond:0.###} duration={shot.DurationSeconds:0.###}.");
                }

                if (shotIndex == 0 && Math.Abs(shot.StartSecond - segment.StartSecond) > ToleranceSeconds)
                {
                    shotInvalid = true;
                    gapCount++;
                    errors.Add($"{episodeType} segment {segment.SegmentId} first shot starts at {shot.StartSecond:0.###} instead of segment start {segment.StartSecond:0.###}.");
                }

                if (shotIndex > 0)
                {
                    var previousShot = segment.Shots[shotIndex - 1];
                    var delta = shot.StartSecond - previousShot.EndSecond;
                    if (delta > ToleranceSeconds)
                    {
                        shotInvalid = true;
                        gapCount++;
                        errors.Add($"{episodeType} segment {segment.SegmentId} has a shot gap of {delta:0.###}s before shot {shot.ShotNumber}.");
                    }
                    else if (delta < -ToleranceSeconds)
                    {
                        shotInvalid = true;
                        overlapCount++;
                        errors.Add($"{episodeType} segment {segment.SegmentId} has a shot overlap of {Math.Abs(delta):0.###}s at shot {shot.ShotNumber}.");
                    }
                }

                if (shotIndex == segment.Shots.Count - 1 && Math.Abs(shot.EndSecond - segment.EndSecond) > ToleranceSeconds)
                {
                    shotInvalid = true;
                    gapCount++;
                    errors.Add($"{episodeType} segment {segment.SegmentId} last shot ends at {shot.EndSecond:0.###} instead of segment end {segment.EndSecond:0.###}.");
                }

                if (shotInvalid) invalidShotCount++;
            }
        }

        var audioTotal = audioSegments.Sum(x => x.ActualAudioDurationSeconds);
        if (audioSegments.Count > 0 && Math.Abs(timeline.ActualDurationSeconds - audioTotal) > AudioDurationToleranceSeconds)
        {
            invalidSegmentCount++;
            errors.Add($"{episodeType} episode duration {timeline.ActualDurationSeconds:0.###}s does not match summed audio duration {audioTotal:0.###}s.");
        }

        if (previousSegmentEnd is not null && Math.Abs(timeline.ActualDurationSeconds - previousSegmentEnd.Value) > ToleranceSeconds)
        {
            invalidSegmentCount++;
            errors.Add($"{episodeType} episode duration {timeline.ActualDurationSeconds:0.###}s does not equal final segment end {previousSegmentEnd.Value:0.###}s.");
        }

        var segmentTimingValid = invalidSegmentCount == 0;
        var shotTimingValid = invalidShotCount == 0;
        var timelineContinuous = gapCount == 0 && overlapCount == 0;
        return new WeeklyAudioDrivenEpisodeTimelineValidationReport(segmentTimingValid, shotTimingValid, timelineContinuous, Round(timeline.ActualDurationSeconds), invalidSegmentCount, invalidShotCount, gapCount, overlapCount);
    }

    private static RenderStoryboardReport BuildStoryboardReport(Guid pipelineRunId, FinalRenderTimeline timeline, RenderStoryboardReport? source)
    {
        var excerpts = (source?.Segments ?? []).GroupBy(s => (s.EpisodeType, s.SegmentType)).ToDictionary(g => g.Key, g => g.First().NarrationExcerpt);
        return new RenderStoryboardReport(pipelineRunId, DateTime.UtcNow, timeline.Longform.Segments.Concat(timeline.Shortform.Segments).Select(segment =>
            new RenderStoryboardSegmentReport(segment.EpisodeType, segment.SegmentType, segment.StartSecond, segment.EndSecond, excerpts.TryGetValue((segment.EpisodeType, segment.SegmentType), out var excerpt) ? excerpt : Truncate(segment.NarrationText, 160), segment.Shots.Select(shot => new RenderStoryboardShotReport(shot.ShotNumber, shot.AssetType, ResolveAssetCode(shot.AssetPath, shot.AssetId), ResolveSceneFamily(shot.AssetPath, shot.AssetId), shot.DurationSeconds, shot.Purpose)).ToList())).ToList());
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions) ?? throw new InvalidOperationException($"Unable to deserialize required audio-driven reconciliation input: {path}");
    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);

    private static WeeklyAudioDrivenReconciliationInputResolutionReport ResolveInputContract(WeeklyAudioDrivenTimelineReconciliationPaths paths)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var audioManifestFound = File.Exists(paths.AudioSegmentManifest);
        var audioTimingValidationFound = File.Exists(paths.AudioTimingValidationReport);
        var finalRenderTimelineFound = File.Exists(paths.FinalRenderTimeline);
        var finalRenderShotListFound = File.Exists(paths.FinalRenderShotList);
        var weeklyRenderContractFound = File.Exists(paths.RenderContract);
        var renderInputManifestFound = File.Exists(paths.RenderInputManifest);
        var resolvedRenderShotPlanFound = File.Exists(paths.ResolvedRenderShotPlan);
        var renderStoryboardReportFound = File.Exists(paths.RenderStoryboardReport);
        var newContractFilesFound = finalRenderTimelineFound && finalRenderShotListFound && weeklyRenderContractFound && renderInputManifestFound;
        var legacyFilesFound = finalRenderTimelineFound && weeklyRenderContractFound && resolvedRenderShotPlanFound && renderStoryboardReportFound;

        if (!audioManifestFound) errors.Add($"Required audio-driven reconciliation input file is missing: {paths.AudioSegmentManifest}");
        if (!audioTimingValidationFound) errors.Add($"Required audio-driven reconciliation input file is missing: {paths.AudioTimingValidationReport}");

        if (newContractFilesFound)
        {
            if (!resolvedRenderShotPlanFound) warnings.Add("Legacy resolved-render-shot-plan.json not found; using new renderer contract.");
            if (!renderStoryboardReportFound) warnings.Add("Legacy render-storyboard-report.json not found; using new renderer contract.");
            return new WeeklyAudioDrivenReconciliationInputResolutionReport(errors.Count == 0, "NewRendererContract", true, resolvedRenderShotPlanFound && renderStoryboardReportFound, finalRenderTimelineFound, finalRenderShotListFound, weeklyRenderContractFound, renderInputManifestFound, warnings, errors);
        }

        if (legacyFilesFound)
        {
            return new WeeklyAudioDrivenReconciliationInputResolutionReport(errors.Count == 0, "LegacyResolvedShotPlan", false, true, finalRenderTimelineFound, finalRenderShotListFound, weeklyRenderContractFound, renderInputManifestFound, warnings, errors);
        }

        AddMissingSetErrors("new renderer contract", [paths.FinalRenderTimeline, paths.FinalRenderShotList, paths.RenderContract, paths.RenderInputManifest], errors);
        AddMissingSetErrors("legacy resolved shot plan", [paths.FinalRenderTimeline, paths.RenderContract, paths.ResolvedRenderShotPlan, paths.RenderStoryboardReport], errors);
        return new WeeklyAudioDrivenReconciliationInputResolutionReport(false, "MissingRequiredInputs", false, false, finalRenderTimelineFound, finalRenderShotListFound, weeklyRenderContractFound, renderInputManifestFound, warnings, errors);
    }

    private static void AddMissingSetErrors(string inputMode, IReadOnlyList<string> paths, List<string> errors)
    {
        var missing = paths.Where(path => !File.Exists(path)).ToList();
        if (missing.Count == 0) return;
        errors.Add($"Required audio-driven reconciliation {inputMode} input set is incomplete; missing: {string.Join(", ", missing)}");
    }

    private static void EnsureOutputWritable(WeeklyAudioDrivenTimelineReconciliationPaths paths, bool overwriteExisting)
    {
        if (overwriteExisting) return;
        foreach (var path in paths.Outputs)
        {
            if (File.Exists(path)) throw new InvalidOperationException($"Audio-driven reconciliation output already exists and overwriteExisting is false: {path}");
        }
    }

    private static ResolvedRenderEpisodeShotPlan? FindEpisodePlan(ResolvedRenderShotPlan plan, string episodeType) => plan.Episodes.FirstOrDefault(x => x.EpisodeType.Equals(episodeType, StringComparison.OrdinalIgnoreCase));
    private static ResolvedRenderEpisodeShotPlan ToShotPlan(string episodeType, FinalRenderEpisodeTimeline timeline) => new(episodeType, timeline.ActualDurationSeconds, timeline.Segments.Select(s => ToShotPlanSegment(episodeType, s)).ToList());
    private static ResolvedRenderSegmentShotPlan ToShotPlanSegment(string episodeType, FinalRenderSegment segment) => new(episodeType, segment.SegmentId, segment.SegmentType, segment.StartSecond, segment.EndSecond, segment.DurationSeconds, segment.Shots.Select(ToResolvedShot).ToList());
    private static FinalRenderShot ToFinalShot(ResolvedRenderShotPlanEntry shot) => new(shot.ShotNumber, shot.AssetId, shot.AssetType, shot.AssetPath, shot.StartSecond, shot.EndSecond, shot.DurationSeconds, shot.TransitionIn, shot.TransitionOut, shot.MotionEffect, shot.Purpose);
    private static ResolvedRenderShotPlanEntry ToResolvedShot(FinalRenderShot shot) => new(shot.ShotNumber, shot.AssetId, shot.AssetType, shot.AssetPath, shot.StartSecond, shot.EndSecond, shot.DurationSeconds, shot.TransitionIn, shot.TransitionOut, shot.MotionEffect, shot.Purpose, "AudioDriven", false, false);
    private static bool SameShot(FinalRenderShot left, FinalRenderShot right) => left.ShotNumber == right.ShotNumber && string.Equals(left.AssetPath, right.AssetPath, StringComparison.OrdinalIgnoreCase) && string.Equals(left.AssetId, right.AssetId, StringComparison.OrdinalIgnoreCase);
    private static bool IsRepeatableShot(FinalRenderShot shot) => ContainsAny(shot.AssetPath + " " + shot.AssetId + " " + shot.AssetType, "cinematic", "background", "ai-cinematic", "motion-graphics") || shot.AssetType.Equals("AICinematic", StringComparison.OrdinalIgnoreCase) || shot.AssetType.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase);
    private static bool IsWesternGroupingPath(string value) => IsDynamicGroupingShot(value);
    private static bool IsWesternGroupingParentShot(string value) => ContainsAny(value, "western_planet_grouping_scene", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide");
    private static bool IsDynamicGroupingShot(string value)
    {
        if (IsWesternGroupingParentShot(value)) return true;
        if (ContainsAny(value, "parentSceneCode=western_planet_grouping_scene", "sourceSceneCode=western_planet_grouping_scene", "sourceSceneCode\":\"western_planet_grouping_scene", "parentSceneCode\":\"western_planet_grouping_scene")) return true;
        if (ContainsAny(value, "focusGroupings", "focus_groupings") && ContainsAny(value, "venus", "saturn", "moon")) return true;
        return false;
    }
    private static bool IsGroupingFocusShot(string value, string focusObject) => IsDynamicGroupingShot(value) && value.Contains(focusObject, StringComparison.OrdinalIgnoreCase);
    private static bool IsMoonHeroContextShot(string value) => ContainsAny(value, "moon_hero_scene", "moon hero", "moon-hero");
    private static bool IsShortformGroupingPreservationShot(string value, string segmentType) => IsDynamicGroupingShot(value) || IsMoonHeroContextShot(value) || (ContainsAny(value, "hero-event-card", "hero_event_card") && IsDynamicGroupingShot(value + " " + segmentType));
    private static bool IsCtaVisual(string value, string segmentType) =>
        ContainsAny(value, "cta", "call_to_action", "call-to-action", "shortform_call_to_action_background") ||
        (segmentType.Equals("CallToAction", StringComparison.OrdinalIgnoreCase) && ContainsAny(value, "AICinematic", "ai-cinematic")) ||
        (segmentType.Equals("CallToAction", StringComparison.OrdinalIgnoreCase) && ContainsAny(value, "background")) ||
        segmentType.Equals("Closing", StringComparison.OrdinalIgnoreCase) ||
        ContainsAny(value, "role=CTA", "role:CTA", "scene role CTA", "sceneRole=CTA", "Closing");
    private static void AddGroupingChildSceneCode(string value, SortedSet<string> childSceneCodes)
    {
        if (value.Contains("western_planet_grouping_scene_saturn", StringComparison.OrdinalIgnoreCase)) childSceneCodes.Add("western_planet_grouping_scene_saturn");
        if (value.Contains("western_planet_grouping_scene_venus", StringComparison.OrdinalIgnoreCase)) childSceneCodes.Add("western_planet_grouping_scene_venus");
    }
    private static bool ContainsAny(string value, params string[] needles) => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    private static string ShotText(ResolvedRenderShotPlanEntry shot) => shot.AssetPath + " " + shot.AssetId + " " + shot.AssetType + " " + shot.Purpose;
    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static string Truncate(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];
    private static string ResolveAssetCode(string assetPath, string assetId) => !string.IsNullOrWhiteSpace(assetId) ? assetId : Path.GetFileNameWithoutExtension(assetPath);
    private static string ResolveSceneFamily(string assetPath, string assetId)
    {
        var value = assetPath + " " + assetId;
        if (IsWesternGroupingPath(value)) return "western_planet_grouping_scene";
        if (value.Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase)) return "moon_hero_scene";
        if (ContainsAny(value, "astrophotography_target_scene", "ExpandedStellarium")) return "ExpandedStellarium";
        return Path.GetFileName(Path.GetDirectoryName(assetPath) ?? string.Empty) ?? string.Empty;
    }

    private sealed record EpisodeReconciliationResult(FinalRenderEpisodeTimeline Timeline, ResolvedRenderEpisodeShotPlan ShotPlan, double NewDurationSeconds, int SegmentsReconciled);

    private sealed record WeeklyAudioDrivenTimelineReconciliationPaths(
        string AudioSegmentManifest,
        string AudioTimingValidationReport,
        string LongformCombinedAudio,
        string ShortformCombinedAudio,
        string FinalRenderTimeline,
        string FinalRenderShotList,
        string ResolvedRenderShotPlan,
        string RenderStoryboardReport,
        string RenderContract,
        string RenderInputManifest,
        string AudioDrivenFinalRenderTimeline,
        string AudioDrivenResolvedRenderShotPlan,
        string AudioDrivenStoryboardReport,
        string AudioDrivenRenderContract,
        string AudioDrivenTimelineReconciliationReport,
        string AudioDrivenTimelineValidationReport,
        string AudioDrivenReconciliationInputResolutionReport)
    {
        public IReadOnlyList<string> Outputs => [AudioDrivenFinalRenderTimeline, AudioDrivenResolvedRenderShotPlan, AudioDrivenStoryboardReport, AudioDrivenRenderContract, AudioDrivenTimelineReconciliationReport, AudioDrivenTimelineValidationReport, AudioDrivenReconciliationInputResolutionReport];
        public static WeeklyAudioDrivenTimelineReconciliationPaths FromRoot(string root) => new(
            Path.Combine(root, "audio", "audio-segment-manifest.json"),
            Path.Combine(root, "audio", "audio-timing-validation-report.json"),
            Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3"),
            Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3"),
            Path.Combine(root, "episode", "final-render-timeline.json"),
            Path.Combine(root, "episode", "final-render-shot-list.json"),
            Path.Combine(root, "render", "resolved-render-shot-plan.json"),
            Path.Combine(root, "render", "render-storyboard-report.json"),
            Path.Combine(root, "render", "weekly-render-contract.json"),
            Path.Combine(root, "render", "render-input-manifest.json"),
            Path.Combine(root, "render", "audio-driven-final-render-timeline.json"),
            Path.Combine(root, "render", "audio-driven-resolved-render-shot-plan.json"),
            Path.Combine(root, "render", "audio-driven-render-storyboard-report.json"),
            Path.Combine(root, "render", "audio-driven-render-contract.json"),
            Path.Combine(root, "render", "audio-driven-timeline-reconciliation-report.json"),
            Path.Combine(root, "render", "audio-driven-timeline-validation-report.json"),
            Path.Combine(root, "render", "audio-driven-reconciliation-input-resolution-report.json"));
    }
}
