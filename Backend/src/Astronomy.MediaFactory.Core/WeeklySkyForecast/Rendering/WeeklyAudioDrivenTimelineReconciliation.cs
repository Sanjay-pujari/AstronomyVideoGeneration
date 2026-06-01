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

public sealed record WeeklyAudioDrivenTimelineValidationReport(
    Guid PipelineRunId,
    bool AudioDrivenTimelineValid,
    WeeklyAudioDrivenEpisodeTimelineValidationReport Longform,
    WeeklyAudioDrivenEpisodeTimelineValidationReport Shortform,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

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
        ValidateRequiredInputs(paths, errors);
        if (errors.Count > 0) throw new FileNotFoundException(string.Join(" ", errors));

        var manifest = await ReadJsonAsync<WeeklyAudioSegmentManifest>(paths.AudioSegmentManifest, cancellationToken);
        _ = await ReadJsonAsync<WeeklyAudioTimingValidationReport>(paths.AudioTimingValidationReport, cancellationToken);
        var sourceTimeline = await ReadJsonAsync<FinalRenderTimeline>(paths.FinalRenderTimeline, cancellationToken);
        var sourceShotPlan = await ReadJsonAsync<ResolvedRenderShotPlan>(paths.ResolvedRenderShotPlan, cancellationToken);
        var sourceStoryboard = await ReadJsonAsync<RenderStoryboardReport>(paths.RenderStoryboardReport, cancellationToken);
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

        ValidateMandatoryShots(reconciledShotPlan, errors);

        var validationReport = ValidateAudioDrivenTimeline(pipelineRunId, reconciledTimeline, request.ReconcileLongform ? manifest.Longform : [], request.ReconcileShortform ? manifest.Shortform : [], warnings);

        var allErrors = errors.Concat(validationReport.Errors).ToList();
        var longformAudioTotal = request.ReconcileLongform ? manifest.Longform.Sum(x => x.ActualAudioDurationSeconds) : longform.NewDurationSeconds;
        var shortformAudioTotal = request.ReconcileShortform ? manifest.Shortform.Sum(x => x.ActualAudioDurationSeconds) : shortform.NewDurationSeconds;
        var videoDurationNowMatchesAudio = Math.Abs(longform.NewDurationSeconds - longformAudioTotal) <= AudioDurationToleranceSeconds && Math.Abs(shortform.NewDurationSeconds - shortformAudioTotal) <= AudioDurationToleranceSeconds;
        if (!videoDurationNowMatchesAudio) allErrors.Add("Audio-driven video duration does not match audio manifest duration.");

        var ready = allErrors.Count == 0 && validationReport.AudioDrivenTimelineValid && videoDurationNowMatchesAudio;
        var report = new WeeklyAudioDrivenTimelineReconciliationReport(
            pipelineRunId,
            ready,
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
            return ContainsAny(haystack, "hook", "fast_cinematic_sky_hook") || IsWesternGroupingPath(haystack) || ContainsAny(haystack, "cta", "call_to_action", "call-to-action", "shortform_call_to_action_background");
        }
        return segmentType switch
        {
            "HeroEvent" or "StrongestEvent" => IsWesternGroupingPath(haystack),
            "PlanetHighlights" => IsWesternGroupingPath(haystack),
            "MoonHighlights" => haystack.Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase),
            "AstrophotographyTip" => ContainsAny(haystack, "astrophotography_target_scene", "ExpandedStellarium"),
            "BestObservationWindow" => ContainsAny(haystack, "best-observation-window-card", "best-time-card"),
            "WeeklySummary" => ContainsAny(haystack, "closing", "weekly-summary-card", "cosmic_closing_background"),
            _ => false
        };
    }

    private static void ValidateMandatoryShots(ResolvedRenderShotPlan shotPlan, List<string> errors)
    {
        foreach (var episode in shotPlan.Episodes)
        {
            if (episode.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase))
            {
                var shots = episode.Segments.SelectMany(s => s.Shots).ToList();
                if (!shots.Any(s => ContainsAny(ShotText(s), "hook", "fast_cinematic_sky_hook"))) errors.Add("Shortform hook visual was not preserved.");
                if (shots.Count(s => IsWesternGroupingPath(ShotText(s))) < 2) errors.Add("Shortform must preserve at least 2 grouping shots.");
                if (!shots.Any(s => ContainsAny(ShotText(s), "cta", "call_to_action", "call-to-action", "shortform_call_to_action_background"))) errors.Add("Shortform CTA visual was not preserved.");
                continue;
            }

            foreach (var segment in episode.Segments)
            {
                var shots = segment.Shots;
                if ((segment.SegmentType is "HeroEvent" or "StrongestEvent") && shots.Count(s => IsWesternGroupingPath(ShotText(s))) < 3) errors.Add("HeroEvent must preserve at least 3 western_planet_grouping_scene frames.");
                if (segment.SegmentType.Equals("PlanetHighlights", StringComparison.OrdinalIgnoreCase) && shots.Count(s => IsWesternGroupingPath(ShotText(s))) < 2) errors.Add("PlanetHighlights must preserve at least 2 western_planet_grouping_scene frames.");
                if (segment.SegmentType.Equals("MoonHighlights", StringComparison.OrdinalIgnoreCase) && shots.Count(s => ShotText(s).Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase)) < 2) errors.Add("MoonHighlights must preserve at least 2 moon_hero_scene frames.");
                if (segment.SegmentType.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase) && !shots.Any(s => ContainsAny(ShotText(s), "astrophotography_target_scene", "ExpandedStellarium"))) errors.Add("AstrophotographyTip must preserve an ExpandedStellarium frame.");
                if (segment.SegmentType.Equals("BestObservationWindow", StringComparison.OrdinalIgnoreCase) && !shots.Any(s => ContainsAny(ShotText(s), "best-observation-window-card", "best-time-card"))) errors.Add("BestObservationWindow must preserve a best-observation-window-card or best-time-card visual.");
                if (segment.SegmentType.Equals("WeeklySummary", StringComparison.OrdinalIgnoreCase) && !shots.Any(s => ContainsAny(ShotText(s), "closing", "weekly-summary-card", "cosmic_closing_background"))) errors.Add("WeeklySummary must preserve closing cinematic or weekly-summary-card visual.");
            }
        }
    }

    private static WeeklyAudioDrivenTimelineValidationReport ValidateAudioDrivenTimeline(Guid pipelineRunId, FinalRenderTimeline timeline, IReadOnlyList<WeeklyAudioSegmentManifestEntry> longformAudioSegments, IReadOnlyList<WeeklyAudioSegmentManifestEntry> shortformAudioSegments, IReadOnlyList<string> warnings)
    {
        var errors = new List<string>();
        var longform = ValidateAudioDrivenEpisodeTimeline("longform", timeline.Longform, longformAudioSegments, errors);
        var shortform = ValidateAudioDrivenEpisodeTimeline("shortform", timeline.Shortform, shortformAudioSegments, errors);

        return new WeeklyAudioDrivenTimelineValidationReport(
            pipelineRunId,
            longform.SegmentTimingValid && longform.ShotTimingValid && longform.TimelineContinuous && shortform.SegmentTimingValid && shortform.ShotTimingValid && shortform.TimelineContinuous,
            longform,
            shortform,
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

    private static RenderStoryboardReport BuildStoryboardReport(Guid pipelineRunId, FinalRenderTimeline timeline, RenderStoryboardReport source)
    {
        var excerpts = source.Segments.GroupBy(s => (s.EpisodeType, s.SegmentType)).ToDictionary(g => g.Key, g => g.First().NarrationExcerpt);
        return new RenderStoryboardReport(pipelineRunId, DateTime.UtcNow, timeline.Longform.Segments.Concat(timeline.Shortform.Segments).Select(segment =>
            new RenderStoryboardSegmentReport(segment.EpisodeType, segment.SegmentType, segment.StartSecond, segment.EndSecond, excerpts.TryGetValue((segment.EpisodeType, segment.SegmentType), out var excerpt) ? excerpt : Truncate(segment.NarrationText, 160), segment.Shots.Select(shot => new RenderStoryboardShotReport(shot.ShotNumber, shot.AssetType, ResolveAssetCode(shot.AssetPath, shot.AssetId), ResolveSceneFamily(shot.AssetPath, shot.AssetId), shot.DurationSeconds, shot.Purpose)).ToList())).ToList());
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions) ?? throw new InvalidOperationException($"Unable to deserialize required audio-driven reconciliation input: {path}");
    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);

    private static void ValidateRequiredInputs(WeeklyAudioDrivenTimelineReconciliationPaths paths, List<string> errors)
    {
        foreach (var path in paths.RequiredJsonInputs)
        {
            if (!File.Exists(path)) errors.Add($"Required audio-driven reconciliation input file is missing: {path}");
        }
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
    private static bool IsWesternGroupingPath(string value) => ContainsAny(value, "western_planet_grouping_scene", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide");
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
        string ResolvedRenderShotPlan,
        string RenderStoryboardReport,
        string RenderContract,
        string AudioDrivenFinalRenderTimeline,
        string AudioDrivenResolvedRenderShotPlan,
        string AudioDrivenStoryboardReport,
        string AudioDrivenRenderContract,
        string AudioDrivenTimelineReconciliationReport,
        string AudioDrivenTimelineValidationReport)
    {
        public IReadOnlyList<string> RequiredJsonInputs => [AudioSegmentManifest, AudioTimingValidationReport, FinalRenderTimeline, ResolvedRenderShotPlan, RenderStoryboardReport, RenderContract];
        public IReadOnlyList<string> Outputs => [AudioDrivenFinalRenderTimeline, AudioDrivenResolvedRenderShotPlan, AudioDrivenStoryboardReport, AudioDrivenRenderContract, AudioDrivenTimelineReconciliationReport, AudioDrivenTimelineValidationReport];
        public static WeeklyAudioDrivenTimelineReconciliationPaths FromRoot(string root) => new(
            Path.Combine(root, "audio", "audio-segment-manifest.json"),
            Path.Combine(root, "audio", "audio-timing-validation-report.json"),
            Path.Combine(root, "audio", "longform", "weekly-skyforecast-longform.mp3"),
            Path.Combine(root, "audio", "shortform", "weekly-skyforecast-shortform.mp3"),
            Path.Combine(root, "episode", "final-render-timeline.json"),
            Path.Combine(root, "render", "resolved-render-shot-plan.json"),
            Path.Combine(root, "render", "render-storyboard-report.json"),
            Path.Combine(root, "render", "weekly-render-contract.json"),
            Path.Combine(root, "render", "audio-driven-final-render-timeline.json"),
            Path.Combine(root, "render", "audio-driven-resolved-render-shot-plan.json"),
            Path.Combine(root, "render", "audio-driven-render-storyboard-report.json"),
            Path.Combine(root, "render", "audio-driven-render-contract.json"),
            Path.Combine(root, "render", "audio-driven-timeline-reconciliation-report.json"),
            Path.Combine(root, "render", "audio-driven-timeline-validation-report.json"));
    }
}
